import re, glob, os

def read(p):
    with open(p, 'r', encoding='utf-8', errors='replace') as f:
        return f.read()

# guid -> script path
guid_map = {}
for meta in glob.glob('Assets/**/*.cs.meta', recursive=True):
    mc = read(meta)
    m = re.search(r'guid:\s*([0-9a-f]{32})', mc)
    if m:
        guid_map[m.group(1)] = meta[:-5]

def scripts_of(path):
    c = read(path)
    guids = re.findall(r'm_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-f]{32})', c)
    return guids

print("=== 敌人 prefab 上的脚本 ===")
for g in sorted(set(scripts_of('Assets/_Game/character/M_WangLingDaoBing/M_WangLingDaoBing_Enemy.prefab'))):
    print(f'  {g}  ->  {os.path.basename(guid_map.get(g, "???"))}')

print()
print("=== 玩家 prefab 上的脚本 ===")
for g in sorted(set(scripts_of('Assets/_Game/character/h_ezreal/h_ezreal_Character.prefab'))):
    print(f'  {g}  ->  {os.path.basename(guid_map.get(g, "???"))}')
