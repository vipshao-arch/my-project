import functools
import base64
import json
import threading
import time
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from playwright.sync_api import sync_playwright

out = Path('test-results')
out.mkdir(exist_ok=True)
handler = functools.partial(SimpleHTTPRequestHandler, directory=str(Path('tides').resolve()))
server = ThreadingHTTPServer(('127.0.0.1', 0), handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
report = {'passed': False, 'errors': [], 'checks': []}
try:
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True, args=['--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
        page = browser.new_page(viewport={'width': 1280, 'height': 720})
        page.on('console', lambda msg: report['errors'].append(msg.text[:4000]) if msg.type == 'error' else None)
        page.on('pageerror', lambda err: report['errors'].append(str(err)))
        page.goto(f'http://127.0.0.1:{server.server_port}/?verify=1', timeout=120000)
        page.wait_for_function('window.__tidesReadback?.state === 1', timeout=120000)
        session = page.context.new_cdp_session(page)

        def capture(name):
            image = session.send('Page.captureScreenshot', {'format': 'png', 'fromSurface': True})
            (out / (name + '.png')).write_bytes(base64.b64decode(image['data']))

        capture('port')
        report['checks'].append('Normal startup reached port with the latest native UI')

        def click_action(action, argument=None):
            data = page.evaluate('window.__tidesReadback')
            buttons = [b for b in data['buttons'] if b['action'] == action and
                       (argument is None or b['argument'] == argument)]
            assert buttons, f'Missing visible native action: {action} {argument}'
            button = buttons[0]
            rect = page.locator('canvas').bounding_box()
            page.mouse.click(rect['x'] + button['x'] * rect['width'] / data['viewport'][0],
                             rect['y'] + button['y'] * rect['height'] / data['viewport'][1])

        click_action('sail')
        page.wait_for_function('window.__tidesReadback?.state === 0', timeout=15000)
        position = page.evaluate('window.__tidesReadback.position')
        page.wait_for_function('window.__tidesReadback.speed > 0.1', timeout=15000)
        page.wait_for_timeout(1500)
        moved = page.evaluate('window.__tidesReadback.position')
        assert sum((a-b)**2 for a,b in zip(position, moved)) > 0.1, 'Ship did not move after sailing'
        capture('sailing')
        report['checks'].append('Native sail button entered sailing and the ship moved')
        click_action('anchor')
        page.wait_for_function('window.__tidesReadback.anchored && window.__tidesReadback.speed === 0', timeout=10000)
        capture('anchor')
        report['checks'].append('Native anchor button stopped the ship')
        click_action('system', 'combat')
        page.wait_for_function("window.__tidesReadback.system === 'combat'", timeout=10000)
        capture('combat')
        report['checks'].append('Native combat button switched to combat layout')
        fatal = [e for e in report['errors'] if any(s in e for s in
                 ('SCRIPT ERROR', 'SHADER ERROR', 'Parse Error', 'Failed to load script', 'shader compilation failed',
                  'No loader found for resource', 'Error loading project data'))]
        assert not fatal, '\n'.join(fatal[:8])
        report['passed'] = True
        browser.close()
except Exception as error:
    report['failure'] = str(error)
    try:
        report['readback'] = page.evaluate('window.__tidesReadback')
        capture('failure')
    except Exception:
        pass
    raise
finally:
    server.shutdown()
    (out / 'result.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
