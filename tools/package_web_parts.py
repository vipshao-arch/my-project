"""Build a deployable Web directory with SHA-256 verified 24 MiB pack chunks.

The loader streams parts sequentially, reports byte progress, retries transient
failures, and displays readable Chinese notices instead of freezing at 14%.
"""
from pathlib import Path
import hashlib
import json
import shutil

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "web_build"
DEST = ROOT / "dist" / "web_20261117"

import json as _json
LOADER = _json.loads('"async function sha256Hex(buffer) {\\n  const digest = await crypto.subtle.digest(\'SHA-256\', buffer);\\n  return Array.from(new Uint8Array(digest)).map(x => x.toString(16).padStart(2, \'0\')).join(\'\');\\n}\\n\\nasync function fetchPart(part, index, totalParts, options, completedBytes) {\\n  let lastError;\\n  for (let attempt = 0; attempt < 4; attempt++) {\\n    try {\\n      const url = `${part.name}?v=${part.sha256.slice(0, 16)}&retry=${attempt}`;\\n      const response = await fetch(url, {cache: \'no-store\'});\\n      if (!response.ok) throw new Error(`HTTP ${response.status}`);\\n      const declared = Number(response.headers.get(\'content-length\')) || part.bytes;\\n      const reader = response.body ? response.body.getReader() : null;\\n      let bytes;\\n      if (reader) {\\n        const chunks = [];\\n        let received = 0;\\n        while (true) {\\n          const step = await reader.read();\\n          if (step.done) break;\\n          chunks.push(step.value);\\n          received += step.value.byteLength;\\n          options.onProgress(completedBytes + Math.min(received, part.bytes), options.totalBytes);\\n        }\\n        bytes = new Uint8Array(received);\\n        let offset = 0;\\n        for (const chunk of chunks) {\\n          bytes.set(chunk, offset);\\n          offset += chunk.byteLength;\\n        }\\n      } else {\\n        bytes = new Uint8Array(await response.arrayBuffer());\\n        options.onProgress(completedBytes + part.bytes, options.totalBytes);\\n      }\\n      if (bytes.length !== part.bytes || declared !== part.bytes) {\\n        throw new Error(`\\u6587\\u4ef6\\u957f\\u5ea6 ${bytes.length}\\uff0c\\u671f\\u671b ${part.bytes}`);\\n      }\\n      const digest = await sha256Hex(bytes);\\n      if (digest !== part.sha256) throw new Error(\'SHA-256 \\u4e0d\\u5339\\u914d\');\\n      options.onProgress(completedBytes + part.bytes, options.totalBytes);\\n      return bytes;\\n    } catch (error) {\\n      lastError = error;\\n      options.onProgress(completedBytes, options.totalBytes);\\n      if (attempt < 3) {\\n        options.onNotice(`\\u7b2c ${index + 1}/${totalParts} \\u6bb5\\u52a0\\u8f7d\\u5931\\u8d25\\uff0c\\u6b63\\u5728\\u91cd\\u8bd5 ${attempt + 1}/3`);\\n        await new Promise(resolve => setTimeout(resolve, 500 * (attempt + 1)));\\n      }\\n    }\\n  }\\n  throw new Error(`${part.name} \\u52a0\\u8f7d\\u5931\\u8d25\\uff1a${lastError ? lastError.message : \'\\u672a\\u77e5\\u9519\\u8bef\'}`);\\n}\\n\\nasync function startPackedGame(options) {\\n  const manifestResponse = await fetch(\'index.pck.parts.json\', {cache: \'no-store\'});\\n  if (!manifestResponse.ok) throw new Error(\'\\u65e0\\u6cd5\\u83b7\\u53d6\\u6e38\\u620f\\u8d44\\u6e90\\u6e05\\u5355\');\\n  const manifest = await manifestResponse.json();\\n  options.totalBytes = manifest.bytes;\\n  options.onProgress(0, manifest.bytes);\\n  const initialize = engine.init(\'index\');\\n  const buffers = [];\\n  let completedBytes = 0;\\n  // Sequential downloads keep memory bounded and make progress/retry state clear.\\n  // Each chunk reports real byte progress instead of one jump after SHA-256.\\n  for (let index = 0; index < manifest.parts.length; index++) {\\n    const part = manifest.parts[index];\\n    options.onNotice(`\\u6b63\\u5728\\u52a0\\u8f7d\\u6d77\\u57df\\u8d44\\u6e90 ${index + 1}/${manifest.parts.length}`);\\n    const bytes = await fetchPart(part, index, manifest.parts.length, options, completedBytes);\\n    buffers.push(bytes);\\n    completedBytes += part.bytes;\\n  }\\n  options.onNotice(\'\\u6b63\\u5728\\u6821\\u9a8c\\u5e76\\u51c6\\u5907\\u542f\\u822a\');\\n  const blob = new Blob(buffers, {type: \'application/octet-stream\'});\\n  const packUrl = URL.createObjectURL(blob);\\n  await engine.preloadFile(packUrl, \'index.pck\');\\n  URL.revokeObjectURL(packUrl);\\n  await initialize;\\n  return engine.start({...options, args: [\'--main-pack\', \'index.pck\']});\\n}\\n"')
NOTICE_HOOK = """\t\t\t'onNotice': function (message) {
\t\t\t\tsetStatusNotice(message);
\t\t\t},
"""


def main() -> None:
    if DEST.exists():
        shutil.rmtree(DEST)
    DEST.mkdir(parents=True)
    for source in SOURCE.iterdir():
        if source.is_file() and source.name.startswith("index.") and source.name != "index.pck":
            shutil.copy2(source, DEST / source.name)
    data = (SOURCE / "index.pck").read_bytes()
    parts = []
    for index, offset in enumerate(range(0, len(data), 24 * 1024 * 1024)):
        name = f"index.pck.part{index:02d}"
        content = data[offset:offset + 24 * 1024 * 1024]
        (DEST / name).write_bytes(content)
        parts.append({"name": name, "bytes": len(content),
                      "sha256": hashlib.sha256(content).hexdigest()})
    descriptor = {"bytes": len(data),
                  "sha256": hashlib.sha256(data).hexdigest(), "parts": parts}
    (DEST / "index.pck.parts.json").write_text(
        json.dumps(descriptor, indent=2), encoding="utf-8")

    html = (DEST / "index.html").read_text(encoding="utf-8")
    if "engine.startGame({" not in html:
        raise SystemExit("Unexpected export HTML")
    if "function setStatusNotice(" not in html:
        raise SystemExit("Unexpected export HTML: missing status notice helper")
    html = html.replace('<html lang="en">', '<html lang="zh-CN">')
    html = html.replace('const engine = new Engine(GODOT_CONFIG);',
                        'const engine = new Engine(GODOT_CONFIG);\n' + LOADER, 1)
    html = html.replace('engine.startGame({', 'startPackedGame({', 1)
    on_progress_end = "\t\t\t},\n\t\t}).then(() => {"
    if on_progress_end not in html:
        raise SystemExit("Unexpected export HTML: missing onProgress hook")
    html = html.replace(on_progress_end,
                        "\t\t\t},\n" + NOTICE_HOOK + "\t\t}).then(() => {", 1)
    (DEST / "index.html").write_text(html, encoding="utf-8")
    (DEST / ".nojekyll").write_text("", encoding="utf-8")
    (DEST / "README.txt").write_text(
        "Serve this directory over HTTP. The game loads SHA-256 verified pack chunks.\n",
        encoding="utf-8",
    )
    print(f"created {DEST} with {len(parts)} chunks, {len(data) / 1024 / 1024:.1f} MiB")


if __name__ == "__main__":
    main()
