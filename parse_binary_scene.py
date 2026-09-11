import re

def read_bin(p):
    with open(p, 'rb') as f:
        return f.read()

def ascii_strings(data, min_len=4):
    """提取可读 ASCII 字符串（含 null 结尾信息）"""
    strs = []
    cur = []
    start = 0
    for i, b in enumerate(data):
        if 32 <= b < 127:
            if not cur:
                start = i
            cur.append(chr(b))
        else:
            if len(cur) >= min_len:
                strs.append((start, ''.join(cur)))
            cur = []
    if len(cur) >= min_len:
        strs.append((start, ''.join(cur)))
    return strs

scene = 'Assets/_Game/TestScene/h_ezreal_test.unity'
data = read_bin(scene)
print(f'场景 {scene} 大小: {len(data)} bytes')

strs = ascii_strings(data)

# 找关键字符串
keywords = ['m_Layer', 'targetLayer', 'm_Bits', 'M_WangLingDaoBing_Enemy', 'h_ezreal', 'EnemyAI', 'hitMask']
for kw in keywords:
    idxs = [i for i, s in strs if s == kw]
    print(f'{kw!r}: 出现 {len(idxs)} 次, 位置={idxs[:20]}')

print()
# 提取 m_Layer 后面的字节（判断 value）
print('=== m_Layer 上下文（null 结尾 + 后续字节） ===')
pos = 0
count = 0
while count < 20:
    idx = data.find(b'm_Layer\x00', pos)
    if idx < 0:
        break
    # 打印后续 20 字节
    tail = data[idx:idx+20]
    print(f'  @{idx}: {tail!r}')
    pos = idx + 1
    count += 1

print()
print('=== targetLayer 上下文 ===')
pos = 0
count = 0
while count < 10:
    idx = data.find(b'targetLayer', pos)
    if idx < 0:
        break
    tail = data[idx:idx+40]
    print(f'  @{idx}: {tail!r}')
    pos = idx + 1
    count += 1
