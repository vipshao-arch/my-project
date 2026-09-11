import re
from collections import Counter

def read(p):
    with open(p, 'r', encoding='utf-8', errors='replace') as f:
        return f.read()

LAYER_RE = re.compile(r'propertyPath: m_Layer\s*\n\s*value:\s*(-?\d+)')
BITS_RE = re.compile(r'(\w+):\s*\n\s*serializedVersion:\s*\d+\s*\n\s*m_Bits:\s*(-?\d+)')
GO_RE = re.compile(r'--- !u!\d+ &(-?\d+)\nGameObject:\n(.*?)(?=\n--- !u!|\Z)', re.S)

def layer_overrides(content):
    return [int(x) for x in LAYER_RE.findall(content)]

def masks(content):
    return {m.group(1): int(m.group(2)) for m in BITS_RE.finditer(content)}

print("=" * 70)
print("玩家 prefab: h_ezreal_Character.prefab")
print("=" * 70)
c = read('Assets/_Game/character/h_ezreal/h_ezreal_Character.prefab')
lv = layer_overrides(c)
print('m_Layer 覆盖分布:', dict(sorted(Counter(lv).items())))
print('mask 字段:', masks(c))

# Also check its source prefab guid to find base
src = re.findall(r'guid:\s*([0-9a-f]{32})', c)
print('引用的 base prefab guids:', set(src))

print()
print("=" * 70)
print("敌人基础 prefab (被嵌套引用): 找 9c6e62f5f5a70cc45a370dc8bb54a08b")
print("=" * 70)
# Find the .meta whose guid is 9c6e62f5f5a70cc45a370dc8bb54a08b
import glob
for meta in glob.glob('Assets/**/*.meta', recursive=True):
    mc = read(meta)
    if '9c6e62f5f5a70cc45a370dc8bb54a08b' in mc:
        print('base prefab file:', meta[:-5])
