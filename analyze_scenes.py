import re
from collections import Counter

def read(p):
    with open(p, 'rb') as f:
        raw = f.read()
    # 检测二进制场景
    if b'\x00' in raw[:200] and not raw.startswith(b'%YAML'):
        return None
    return raw.decode('utf-8', errors='replace')

LAYER_RE = re.compile(r'propertyPath: m_Layer\s*\n\s*value:\s*(-?\d+)')
BITS_RE = re.compile(r'(\w+):\s*\n\s*serializedVersion:\s*\d+\s*\n\s*m_Bits:\s*(-?\d+)')
GO_RE = re.compile(r'--- !u!\d+ &(-?\d+)\nGameObject:\n(.*?)(?=\n--- !u!|\Z)', re.S)

scenes = [
    'Assets/_Game/TestScene/h_ezreal_test.unity',
    'Assets/_Game/TestScene/pvp_arena.unity',
    'Assets/_Game/TestScene/test_scene.unity',
    'Assets/_Game/TestScene/cover_regression.unity',
]

for sf in scenes:
    c = read(sf)
    print("=" * 70)
    print(sf, '->', 'BINARY' if c is None else f'YAML ({len(c)} chars)')
    print("=" * 70)
    if c is None:
        continue

    # m_Layer overrides (nested prefab instances)
    lv = layer_overrides(c) if False else [int(x) for x in LAYER_RE.findall(c)]
    print('m_Layer 覆盖分布:', dict(sorted(Counter(lv).items())))

    # masks
    masks = {m.group(1): int(m.group(2)) for m in BITS_RE.finditer(c)}
    if masks:
        print('mask 字段:', masks)

    # 顶层 GameObject 及其 layer/名字
    gos = GO_RE.findall(c)
    print(f'顶层 GameObject 数量: {len(gos)}')
    for fid, g in gos:
        lm = re.search(r'm_Layer:\s*(-?\d+)', g)
        nm = re.search(r'm_Name:\s*([^\r\n]+)', g)
        name = nm.group(1).strip() if nm else '?'
        layer = lm.group(1) if lm else '?'
        # 只看有意思的
        if re.search(r'player|ezreal|wangling|enemy|daobing|character|hero|spawn|兵|ez', name, re.I) or layer not in ('0', '?'):
            print(f'  layer={layer:>2}  {name}  (fileID={fid})')

    # PrefabInstance 引用 (guid -> 判断是敌人还是玩家)
    instances = re.findall(r'm_SourcePrefab:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})', c)
    print('PrefabInstance 引用 guids:', set(instances))
    print()
