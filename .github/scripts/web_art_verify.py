import argparse
import base64
import functools
import json
import threading
import psutil
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser()
parser.add_argument('--mode', choices=('gameplay', 'visual', 'ui'), default='gameplay')
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
    page = browser.new_page(viewport={'width': 1280, 'height': 720})
    page.on('console', lambda msg: report['errors'].append(msg.text[:4000]) if msg.type == 'error' else None)
    page.on('pageerror', lambda err: report['errors'].append(str(err)))
    page.on('crash', lambda: report['errors'].append('Browser page crashed'))
    session = page.context.new_cdp_session(page)

    def capture(name):
        image = session.send('Page.captureScreenshot', {'format': 'png', 'fromSurface': True})
        (out / (name + '.png')).write_bytes(base64.b64decode(image['data']))
        check_fatal()

    def click_action(action, argument=None):
        data = page.evaluate('window.__tidesReadback')
        buttons = [b for b in data['buttons'] if b['action'] == action and (argument is None or b['argument'] == argument)]
        assert buttons, f'Missing visible native action: {action} {argument}'
        button = buttons[0]
        rect = page.evaluate("(() => { const r=document.querySelector('canvas').getBoundingClientRect(); return {x:r.x,y:r.y,width:r.width,height:r.height}; })()")
        x = rect['x'] + button['x'] * rect['width'] / data['viewport'][0]
        y = rect['y'] + button['y'] * rect['height'] / data['viewport'][1]
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
    elif mode == 'ui':
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
        page.set_viewport_size({'width': 1280, 'height': 720})
        start = page.evaluate('window.__tidesReadback.frame')
        page.wait_for_function('(start) => window.__tidesReadback.frame >= start + 2', arg=start, timeout=90000, polling=200)
        page.keyboard.press('Escape')
        page.wait_for_function('!window.__tidesReadback.ui.popup_visible', timeout=60000, polling=200)
        click_action('map')
        page.wait_for_function('window.__tidesReadback.state === 4', timeout=60000, polling=200)
        check_panel()
        capture('map')
        page.keyboard.press('Escape')
        page.wait_for_function('window.__tidesReadback.state === 1', timeout=60000, polling=200)
        click_action('sail')
        page.wait_for_function('window.__tidesReadback.state === 0', timeout=90000, polling=200)
        data = page.evaluate('window.__tidesReadback')
        aim = data['ui']['left_aim']
        rect = page.evaluate("(() => { const r=document.querySelector('canvas').getBoundingClientRect(); return {x:r.x,y:r.y,width:r.width,height:r.height}; })()")
        page.mouse.move(rect['x'] + aim[0] * rect['width'] / data['viewport'][0], rect['y'] + aim[1] * rect['height'] / data['viewport'][1])
        page.keyboard.down('q')
        page.wait_for_function('window.__tidesReadback.ui.charge_side === 0 && window.__tidesReadback.ui.charge > 0.02', timeout=60000, polling=200)
        check_panel()
        capture('left_charge')
        page.keyboard.up('q')
        page.wait_for_function('window.__tidesReadback.ui.left_reload > 0 && window.__tidesReadback.ui.charge_side === -1', timeout=60000, polling=100)
        ui = page.evaluate('window.__tidesReadback.ui')
        assert ui['right_reload'] == 0 and ui['reload_time'] > 0
        report['left_reload_after_fire'] = ui
        persist()
        capture('left_reload')
        report['checks'].append('Market panels fit at two browser sizes; real map opens and closes; Q charges/fires with independent left reload and unchanged right readiness')
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
