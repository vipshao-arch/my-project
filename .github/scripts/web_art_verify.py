import argparse
import base64
import functools
import json
import math
import threading
import psutil
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser()
parser.add_argument('--mode', choices=('gameplay', 'visual', 'ui', 'ui_panels', 'ui_combat'), default='gameplay')
mode = parser.parse_args().mode
out = Path('test-results')
out.mkdir(exist_ok=True)
server = ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(SimpleHTTPRequestHandler, directory=str(Path('tides').resolve())))
threading.Thread(target=server.serve_forever, daemon=True).start()
report = {'passed': False, 'mode': mode, 'errors': [], 'checks': [], 'clicks': []}

def persist():
    (out / 'result.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

def check_fatal():
    fatal = [e for e in report['errors'] if any(s in e for s in ('SCRIPT ERROR', 'SHADER ERROR', 'Parse Error',
             'Failed to load script', 'shader compilation failed', 'Shader compilation failed', 'No loader found for resource', 'Error loading project data', 'Maximum call stack size exceeded'))]
    assert not fatal, '\n'.join(fatal[:8])

p = sync_playwright().start()
browser = None
page = None
try:
    browser = p.chromium.launch(headless=True, args=['--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    test_viewport = {'width': 960, 'height': 540} if mode == 'ui_combat' else {'width': 1280, 'height': 720}
    page = browser.new_page(viewport=test_viewport)
    report['browser_viewport'] = test_viewport
    page.on('console', lambda msg: report['errors'].append(msg.text[:4000]) if msg.type == 'error' else None)
    page.on('pageerror', lambda err: report['errors'].append(str(err)))
    page.on('crash', lambda: report['errors'].append('Browser page crashed'))
    session = page.context.new_cdp_session(page)

    def capture(name):
        image = session.send('Page.captureScreenshot', {'format': 'png', 'fromSurface': True, 'optimizeForSpeed': True})
        (out / (name + '.png')).write_bytes(base64.b64decode(image['data']))
        check_fatal()

    def click_action(action, argument=None):
        data = page.evaluate('window.__tidesReadback')
        buttons = [b for b in data['buttons'] if b['action'] == action and (argument is None or b['argument'] == argument)]
        assert buttons, f'Missing visible native action: {action} {argument}'
        button = buttons[0]
        rect = page.evaluate("(() => { const r=document.querySelector('canvas').getBoundingClientRect(); return {x:r.x,y:r.y,width:r.width,height:r.height}; })()")
        # Godot's keep-aspect viewport can letterbox inside the canvas bounds.
        # Use the actual game content rectangle, not the outer black margins.
        scale = min(rect['width'] / data['viewport'][0], rect['height'] / data['viewport'][1])
        game_x = rect['x'] + (rect['width'] - data['viewport'][0] * scale) * .5
        game_y = rect['y'] + (rect['height'] - data['viewport'][1] * scale) * .5
        x = game_x + button['x'] * scale
        y = game_y + button['y'] * scale
        report['clicks'].append({'action': action, 'argument': argument, 'button': button, 'screen': [x, y]})
        persist()
        page.mouse.click(x, y)

    def check_panel():
        data = page.evaluate('window.__tidesReadback')
        ui = data['ui']
        assert not ui['bad_captions'], str(ui['bad_captions'])
        if ui['popup_visible']:
            assert ui['popup_size'][0] <= data['viewport'][0] * 0.5 + 1
            assert ui['popup_size'][0] * ui['popup_size'][1] <= data['viewport'][0] * data['viewport'][1] * 0.5 + 1
        report.setdefault('ui_cases', []).append({'viewport': data['viewport'], **ui})
        persist()

    def wait_state(expression, timeout=120000):
        try:
            page.wait_for_function(expression, timeout=timeout, polling=1000)
        except Exception:
            # Software WebGL can return the state only after its long frame
            # finishes. Accept a fresh observation of the SAME predicate;
            # never repeat the input or turn a missing state into success.
            observed = page.evaluate(expression)
            report.setdefault('delayed_observations', []).append({'predicate': expression, 'matched': bool(observed)})
            persist()
            if not observed:
                raise

    if mode == 'gameplay':
        page.goto(f'http://127.0.0.1:{server.server_port}/?verify=1', timeout=120000)
        page.wait_for_function('window.__tidesReadback?.state === 1', timeout=120000, polling=200)
        capture('port')
        lighting = page.evaluate('window.__tidesReadback.lighting')
        assert lighting and lighting['hdr_exposure'] == 1 and lighting['sun_energy'] < 1.1
        report['lighting'] = lighting
        art = page.evaluate('window.__tidesReadback.art')
        assert art['water_shader'].endswith('water_web.gdshader') and art['seabed_triangles'] > 0 and art['contacts'] > 0
        assert 150 <= art['camera_distance'] <= 185 and art['msaa'] == 1
        report['art'] = art
        report['checks'].append('Normal startup reached port with the Web art profile')
        persist()

        click_action('sail')
        page.wait_for_function('window.__tidesReadback?.state === 0', timeout=90000, polling=200)
        position = page.evaluate('window.__tidesReadback.position')
        page.wait_for_function('window.__tidesReadback.speed > 0.1', timeout=30000, polling=200)
        baseline_frame = page.evaluate('window.__tidesReadback.frame')
        capture('sailing')
        page.wait_for_function('(start) => window.__tidesReadback.frame >= start + 5', arg=baseline_frame, timeout=90000, polling=200)
        moved = page.evaluate('window.__tidesReadback.position')
        assert sum((a-b)**2 for a,b in zip(position, moved)) > 0.1
        capture('sailing_motion')
        assert page.evaluate('window.__tidesReadback.art.wake_samples') > 0
        report['checks'].append('Native sail button moved the ship and generated a world-space wake; two frames captured')
        persist()
        click_action('anchor')
        page.wait_for_function('window.__tidesReadback.anchored && window.__tidesReadback.speed === 0', timeout=30000, polling=200)
        capture('anchor')
        report['checks'].append('Native anchor button stopped the ship')
        persist()
        if page.evaluate('window.__tidesReadback.system') != 'combat':
            click_action('system', 'combat')
        page.wait_for_function("window.__tidesReadback.system === 'combat'", timeout=30000, polling=200)
        capture('combat')
        report['checks'].append('Native combat button switched to combat layout')
        check_panel()
        assert page.evaluate('window.__tidesReadback.ui.merged_navigation && !window.__tidesReadback.ui.weather_card_visible')
    elif mode in ('ui', 'ui_panels'):
        page.goto(f'http://127.0.0.1:{server.server_port}/?verify=1', timeout=120000)
        page.wait_for_function('window.__tidesReadback?.state === 1', timeout=120000, polling=200)
        click_action('tab', 'market')
        page.wait_for_function("window.__tidesReadback.ui.page === 'market'", timeout=60000, polling=200)
        check_panel()
        capture('market')
        page.set_viewport_size({'width': 960, 'height': 640})
        start = page.evaluate('window.__tidesReadback.frame')
        page.wait_for_function('(start) => window.__tidesReadback.frame >= start + 3', arg=start, timeout=90000, polling=200)
        check_panel()
        capture('market_960')
        # Both panel sizes were checked. Keep the smaller fresh viewport for
        # the map screenshot instead of another costly software-GPU resize.
        page.keyboard.press('Escape')
        page.wait_for_function('!window.__tidesReadback.ui.popup_visible', timeout=60000, polling=200)
        click_action('map')
        page.wait_for_function('window.__tidesReadback.state === 4', timeout=60000, polling=200)
        check_panel()
        capture('map')
        page.keyboard.press('Escape')
        page.wait_for_function('window.__tidesReadback.state === 1', timeout=60000, polling=200)
        report['checks'].append('Market panels fit at two browser sizes with no clipped native captions; real world chart opens and closes')
        persist()
    elif mode == 'ui_combat':
        page.goto(f'http://127.0.0.1:{server.server_port}/?verify=1', timeout=120000)
        wait_state('window.__tidesReadback?.state === 1')
        click_action('sail')
        wait_state('window.__tidesReadback.state === 0')
        click_action('anchor')
        wait_state('window.__tidesReadback.anchored && window.__tidesReadback.speed === 0')
        # Finish the normal port-to-ship transition before comparing camera motion.
        wait_state('Math.hypot(...window.__tidesReadback.impact_preview.camera_ship_offset) < 190')

        def move_aim(key):
            data = page.evaluate('window.__tidesReadback')
            aim = data['ui'][key]
            bounds = page.evaluate("(() => { const r=document.querySelector('canvas').getBoundingClientRect(); return {x:r.x,y:r.y,width:r.width,height:r.height}; })()")
            scale = min(bounds['width'] / data['viewport'][0], bounds['height'] / data['viewport'][1])
            x = bounds['x'] + (bounds['width'] - data['viewport'][0] * scale) * .5 + aim[0] * scale
            y = bounds['y'] + (bounds['height'] - data['viewport'][1] * scale) * .5 + aim[1] * scale
            page.mouse.move(x, y)

        baseline = page.evaluate('window.__tidesReadback.impact_preview')
        report['camera_baseline'] = baseline
        capture('camera_before_charge')
        move_aim('left_aim')
        page.keyboard.down('q')
        report['stage'] = 'waiting_for_left_charge'
        persist()
        wait_state('window.__tidesReadback.ui.charge_side === 0 && window.__tidesReadback.impact_preview?.active')
        check_panel()
        capture('left_charge')
        first = page.evaluate('window.__tidesReadback')
        report['impact_low'] = first['impact_preview']
        wait_state('window.__tidesReadback.ui.charge >= 1.35 && window.__tidesReadback.impact_preview.visible')
        full = page.evaluate('window.__tidesReadback')
        report['impact_high'] = full['impact_preview']
        assert full['impact_preview']['surface_outline_count'] >= 3, 'Web sea shader must receive the forecast outline'
        history = full['impact_history']
        report['impact_history'] = history
        assert len(history) >= 2, 'Charge must produce more than one forecast observation'
        assert history[-1]['range'] > history[0]['range'] + 10, 'Landing range must move outward as power increases'
        assert history[-1]['power'] > history[0]['power'], 'Charge forecast must use live power'
        forecast = full['impact_preview']
        assert forecast['camera_distance_setting'] == baseline['camera_distance_setting'], 'Charging must not change the user zoom'
        assert abs(forecast['camera_yaw'] - baseline['camera_yaw']) < .001 and abs(forecast['camera_pitch'] - baseline['camera_pitch']) < .001, 'Charging must not redirect the orbit'
        assert 0 < forecast['camera_lift'] <= 10.001, 'Charge lift is limited to ten meters'
        horizontal = math.hypot(forecast['camera_ship_offset'][0], forecast['camera_ship_offset'][2])
        assert abs(horizontal - baseline['camera_distance_setting'] * math.cos(baseline['camera_pitch'])) < 5, 'Charge must not center camera between ship and landing'
        assert all(x['camera_distance_setting'] == baseline['camera_distance_setting'] and 0 <= x['camera_lift'] <= 10.001 for x in history)
        capture('charge_default_zoom')

        # Move the pointer at full power: angle changes must reshape the actual
        # sea footprint, independently of the charge timer or ship movement.
        before_center = forecast['center']
        move_aim('left_turn_aim')
        wait_state(f'Math.hypot(window.__tidesReadback.impact_preview.center[0] - ({before_center[0]}), window.__tidesReadback.impact_preview.center[1] - ({before_center[1]})) > 8')
        report['pointer_angle_forecast'] = page.evaluate('window.__tidesReadback.impact_preview')
        capture('charge_angle_changed')

        # Zoom is a deliberate player action. The free camera does not force
        # all long-range landing points into the default close ship view.
        page.mouse.move(480, 240)
        for _ in range(10):
            page.mouse.wheel(0, 120)
        wait_state(f'window.__tidesReadback.impact_preview.camera_distance_setting >= {baseline["camera_distance_setting"] + 450}')
        move_aim('left_aim')
        report['manual_zoom_forecast'] = page.evaluate('window.__tidesReadback.impact_preview')
        capture('left_charge_full')
        page.keyboard.up('q')
        report['stage'] = 'waiting_for_left_reload_after_release'
        persist()
        wait_state('window.__tidesReadback.ui.left_reload > 0 && window.__tidesReadback.ui.charge_side === -1')
        ui = page.evaluate('window.__tidesReadback.ui')
        assert ui['right_reload'] == 0 and ui['reload_time'] > 0
        assert not page.evaluate('window.__tidesReadback.impact_preview.active'), 'Release must clear forecast'
        report['left_reload_after_fire'] = ui
        report['checks'].append('Normal Q input moves the real ballistic forecast, displays full-charge gun marks on sea, fires and clears preview; independent left/right reload is retained')
        persist()
        capture('left_reload')
        wait_state('window.__tidesReadback.impact_preview.camera_lift < .2')
        move_aim('right_aim')
        page.keyboard.down('e')
        report['stage'] = 'waiting_for_right_charge'
        persist()
        wait_state('window.__tidesReadback.ui.charge_side === 1 && window.__tidesReadback.impact_preview.active')
        before_yaw = page.evaluate('window.__tidesReadback.impact_preview.camera_yaw')
        before_aim = page.evaluate('window.__tidesReadback.impact_preview.aim')
        page.keyboard.down('ArrowRight')
        wait_state(f'Math.abs(window.__tidesReadback.impact_preview.camera_yaw - ({before_yaw})) > .05')
        page.keyboard.up('ArrowRight')
        wait_state(f'Math.hypot(window.__tidesReadback.impact_preview.aim[0] - ({before_aim[0]}), window.__tidesReadback.impact_preview.aim[1] - ({before_aim[1]})) > 1')
        wait_state('window.__tidesReadback.ui.charge >= 1.35 && window.__tidesReadback.impact_preview.visible')
        right = page.evaluate('window.__tidesReadback')
        report['right_impact_high'] = right['impact_preview']
        assert right['impact_preview']['surface_outline_count'] >= 3
        assert right['impact_preview']['camera_distance_setting'] == report['manual_zoom_forecast']['camera_distance_setting'], 'Manual zoom must survive release and charging the other side'
        assert 0 < right['impact_preview']['camera_lift'] <= 10.001
        capture('right_charge_full')
        # Exercise the normal right-button drag, including vertical orbit input.
        prior_pitch = right['impact_preview']['camera_pitch']
        prior_yaw = right['impact_preview']['camera_yaw']
        page.mouse.move(480, 240)
        page.mouse.down(button='right')
        page.mouse.move(540, 252, steps=6)
        page.mouse.up(button='right')
        wait_state(f'Math.abs(window.__tidesReadback.impact_preview.camera_yaw - ({prior_yaw})) > .1 && Math.abs(window.__tidesReadback.impact_preview.camera_pitch - ({prior_pitch})) > .02')
        report['right_drag_forecast'] = page.evaluate('window.__tidesReadback.impact_preview')
        capture('charge_free_drag')
        move_aim('right_aim')
        page.keyboard.up('e')
        wait_state('window.__tidesReadback.ui.right_reload > 0 && window.__tidesReadback.ui.charge_side === -1 && !window.__tidesReadback.impact_preview.active')
        report['right_reload_after_fire'] = page.evaluate('window.__tidesReadback.ui')
        report['checks'].append('Charging keeps the free orbit and user zoom with at most 10m lift; full-power pointer angle changes move the sea footprint; stationary-pointer aim updates after keyboard camera rotation; right-button drag controls yaw and pitch during charging; Q/E retain independent reload')
        capture('right_reload')
    else:
        page.goto(f'http://127.0.0.1:{server.server_port}/?verify=1&environment_preview=1', timeout=120000)
        report['visual_cases'] = []
        for index, name in enumerate(('sunny_shore', 'low_horizon', 'storm', 'night')):
            if index:
                page.keyboard.press('n')
            page.wait_for_function('(name) => window.__tidesArtPreview?.case === name', arg=name, timeout=150000, polling=200)
            visual = page.evaluate('window.__tidesArtPreview')
            assert visual['seabed_triangles'] > 0 and visual['msaa'] == 1
            capture('art_' + name)
            report['visual_cases'].append(visual)
            persist()
        report['checks'].append('Real environment components rendered in sunny shore, low horizon, storm and night fixtures')
    check_fatal()
    report['passed'] = True
    persist()
except Exception as error:
    report['failure'] = str(error)
    persist()
    try:
        report['readback'] = page.evaluate('window.__tidesReadback || window.__tidesArtPreview')
        capture('failure')
    except Exception as capture_error:
        report['capture_failure'] = str(capture_error)
    persist()
finally:
    persist()
    if browser:
        driver_pid = p._impl_obj._connection._transport._proc.pid
        children = psutil.Process(driver_pid).children(recursive=True)
        for child in reversed(children):
            try:
                child.terminate()
            except psutil.NoSuchProcess:
                pass
        _, alive = psutil.wait_procs(children, timeout=5)
        for child in alive:
            try:
                child.kill()
            except psutil.NoSuchProcess:
                pass
        report['browser_cleanup'] = {'owned_driver_pid': driver_pid, 'descendants_stopped': len(children)}
    try:
        p.stop()
    except Exception as stop_error:
        report['driver_stop_note'] = str(stop_error)
    server.shutdown()
    persist()
    print(json.dumps(report, ensure_ascii=False))
if not report['passed']:
    raise SystemExit(1)
