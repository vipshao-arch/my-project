import base64
import functools
import json
import threading
import psutil
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from playwright.sync_api import sync_playwright

out = Path('test-results')
out.mkdir(exist_ok=True)
server = ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(SimpleHTTPRequestHandler, directory=str(Path('tides').resolve())))
threading.Thread(target=server.serve_forever, daemon=True).start()
report = {'passed': False, 'errors': [], 'checks': [], 'clicks': []}

def persist():
    (out / 'result.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

p = sync_playwright().start()
browser = None
page = None
try:
    browser = p.chromium.launch(headless=True, args=['--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    page = browser.new_page(viewport={'width': 1280, 'height': 720})
    page.on('console', lambda msg: report['errors'].append(msg.text[:4000]) if msg.type == 'error' else None)
    page.on('pageerror', lambda err: report['errors'].append(str(err)))
    page.on('crash', lambda: report['errors'].append('Browser page crashed'))
    page.goto(f'http://127.0.0.1:{server.server_port}/?verify=1', timeout=120000)
    page.wait_for_function('window.__tidesReadback?.state === 1', timeout=120000, polling=200)
    session = page.context.new_cdp_session(page)

    def capture(name):
        image = session.send('Page.captureScreenshot', {'format': 'png', 'fromSurface': True})
        (out / (name + '.png')).write_bytes(base64.b64decode(image['data']))

    capture('port')
    report['checks'].append('Normal startup reached port with the latest native UI')
    persist()

    def click_action(action, argument=None):
        data = page.evaluate('window.__tidesReadback')
        buttons = [b for b in data['buttons'] if b['action'] == action and (argument is None or b['argument'] == argument)]
        assert buttons, f'Missing visible native action: {action} {argument}'
        button = buttons[0]
        rect = page.locator('canvas').bounding_box()
        x = rect['x'] + button['x'] * rect['width'] / data['viewport'][0]
        y = rect['y'] + button['y'] * rect['height'] / data['viewport'][1]
        report['clicks'].append({'action': action, 'argument': argument, 'button': button,
                                 'viewport': data['viewport'], 'canvas': rect, 'screen': [x, y]})
        persist()
        page.mouse.click(x, y)

    click_action('sail')
    page.wait_for_function('window.__tidesReadback?.state === 0', timeout=90000, polling=200)
    position = page.evaluate('window.__tidesReadback.position')
    page.wait_for_function('window.__tidesReadback.speed > 0.1', timeout=30000, polling=200)
    page.wait_for_timeout(1500)
    moved = page.evaluate('window.__tidesReadback.position')
    assert sum((a-b)**2 for a,b in zip(position, moved)) > 0.1, 'Ship did not move after sailing'
    capture('sailing')
    report['checks'].append('Native sail button entered sailing and the ship moved')
    persist()
    click_action('anchor')
    page.wait_for_function('window.__tidesReadback.anchored && window.__tidesReadback.speed === 0', timeout=30000, polling=200)
    capture('anchor')
    report['checks'].append('Native anchor button stopped the ship')
    persist()
    report['before_combat'] = page.evaluate('window.__tidesReadback')
    persist()
    if report['before_combat']['system'] != 'combat':
        click_action('system', 'combat')
    page.wait_for_function("window.__tidesReadback.system === 'combat'", timeout=30000, polling=200)
    capture('combat')
    report['checks'].append('Native combat button switched to combat layout')
    fatal = [e for e in report['errors'] if any(s in e for s in ('SCRIPT ERROR', 'SHADER ERROR', 'Parse Error',
             'Failed to load script', 'shader compilation failed', 'No loader found for resource', 'Error loading project data'))]
    assert not fatal, '\n'.join(fatal[:8])
    report['passed'] = True
    persist()
except Exception as error:
    report['failure'] = str(error)
    persist()
    try:
        report['readback'] = page.evaluate('window.__tidesReadback')
        capture('failure')
    except Exception as capture_error:
        report['capture_failure'] = str(capture_error)
    persist()
finally:
    persist()
    # WebGL shutdown can stall graceful Browser.close on the software renderer.
    # Stop only descendants of this fresh Playwright driver's owned process.
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
    (out / 'result.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False))
if not report['passed']:
    raise SystemExit(1)
