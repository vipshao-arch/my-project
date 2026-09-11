import re, glob, os

def read(p):
    with open(p, 'r', encoding='utf-8', errors='replace') as f:
        return f.read()

ENEMY_AI_GUID = '7649d0737afd7ea4fab414255744ce55'
BITS_RE = re.compile(r'targetLayer:\s*\n\s*serializedVersion:\s*\d+\s*\n\s*m_Bits:\s*(-?\d+)')

print("=== 所有挂有 EnemyAI 组件的 prefab/asset 及其 targetLayer ===")
results = []
for pf in glob.glob('Assets/**/*.prefab', recursive=True):
    c = read(pf)
    if ENEMY_AI_GUID in c:
        bits = BITS_RE.findall(c)
        # also archetype/behaviorData references
        arch = re.findall(r'archetype:\s*\{fileID:\s*(\d+)', c)
        beh = re.findall(r'behaviorData:\s*\{fileID:\s*(\d+)', c)
        results.append((pf, bits, arch, beh))

for pf, bits, arch, beh in results:
    print(f'\n{pf}')
    print(f'  targetLayer m_Bits: {bits}')
    print(f'  archetype fileID: {arch}  behaviorData fileID: {beh}')

print()
print("=== EnemyArchetype / EnemyBehaviorData 资产里的 targetLayer ===")
for a in glob.glob('Assets/**/*.asset', recursive=True):
    c = read(a)
    if 'targetLayer' in c and ('EnemyArchetype' in c or 'EnemyBehaviorData' in c or 'targetLayer' in c):
        bits = BITS_RE.findall(c)
        name = os.path.basename(a)
        if bits:
            print(f'{name}: targetLayer={bits}')
