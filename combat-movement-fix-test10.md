# 战斗移动方向修复 — test10 同步文档

> 目的：把 test11 中「战斗移动方向」系列修复完整移植到 test10。
> 涉及 6 个文件，均在同一目录结构 `Assets/_Game/Scripts/...` 下，可直接对照应用。

---

## 一、问题背景与最终设计定论

**核心设计（一以贯之）：**

1. 战斗状态下，角色根朝向 **实时跟随鼠标 / 攻击方向**（根 ≡ 瞄准）。
2. 下半身动画与 **实际物理位移方向** 都基于「攻击方向 + 实时原始输入」判定：
   - W（输入与攻击同向）= 追击
   - S（反向）= 边退边打
   - A / D = 侧移
   - 八向切换均即时响应，不滑行、不滞后。
3. **攻击方向 `_targetDirection` 是唯一上游**，根朝向（`transform.rotation`）、物理位移方向（`CharacterMotor.CombatFacing`）、下半身八向动画（`InputVertical/InputHorizontal`）都由它驱动，要求 **同源同帧、零相位差**。

**历史上踩过的四个坑（本次修复全部覆盖）：**

| 坑 | 根因 | 修复手段 |
| --- | --- | --- |
| 位移方向滞后/错乱 | `CombatFacing` 在 `Update`（渲染帧）写，物理步读到上一帧值 | 整体移到 `FixedUpdate`，配合执行顺序保证同帧 |
| 攻击朝向被摄像机平滑污染 | 摄像机位置 `Lerp` + 旋转 `SmoothDamp(0.08s)` | `GetAimDirection` 用硬贴位置 + 未平滑目标角直算 |
| 移动攻击期间根朝向冻结 | `attackPoseActive` 对移动攻击误判为真且无 `_lastAttackFacing` | 用 `pinnedToAttackFacing = attackPoseActive && _hasLastAttackFacing` 区分 |
| 攻击起手瞬间位移方向跳变 | 起手只更新 `_targetDirection` 未写 `CombatFacing`，残留旧值 | 起手当帧把 `CombatFacing` 钉到攻击朝向 |

---

## 二、涉及文件总览

| 文件 | 改动类型 | 关键点 |
| --- | --- | --- |
| `Skill/SkillController.cs` | 核心 | `FixedUpdate` 写 CombatFacing、`ApplyCombatRotation` 同源、起手钉朝向、攻击后朝向保持 |
| `Character/CharacterMotor.cs` | 位移+动画 | `CombatFacing` 字段、Strafe 速度方向改用 CombatFacing、动画参数直读原始轴 |
| `Character/TopdownCameraController.cs` | 瞄准 | 新增 `GetAimDirection` 去平滑 |
| `Character/CharacterInputHandler.cs` | 输入 | 战斗模式 `RemapCombatInput` clamp 直写 |
| `Character/Input/CharacterInputSources.cs` | 输入源 | `MoveAxesRaw`、`MousePosition` 无平滑直读 |
| `Character/ICharacterMotor.cs` | 接口 | 新增 `CombatFacing` 契约 |

---

## 三、逐文件改动清单

### 1. `Assets/_Game/Scripts/Skill/SkillController.cs`

#### 1.1 类声明带执行顺序（务必确认 test10 已有）

```csharp
[DefaultExecutionOrder(-10)]
public class SkillController : MonoBehaviour
{
    ...
}
```

> 关键：`-10` 让 `SkillController.FixedUpdate` **先于** `CharacterMotor.FixedUpdate`（默认 0）执行，保证同一物理步内先写 `CombatFacing`，Motor 再用最新值算速度。

#### 1.2 新增字段（若 test10 缺）

```csharp
public bool enableAttackFacingHold = true;   // 开关：战斗朝向锁定/实时跟随
public bool debugAttackFacing = false;       // 调试日志开关

private bool   _inCombatMode = false;
private bool   _movingAttackIntent = false;
private Vector3 _targetDirection;             // 攻击方向（唯一上游）
private Vector3 _lastAttackFacing;            // 静止普攻起手锁定的朝向
private bool   _hasLastAttackFacing;
private bool   _postAttackFacingHeld;         // 攻击结束且无移动输入 → 保持朝向
```

#### 1.3 `Update()` 移除 `SyncCombatFacing()` 调用（旧代码）

```csharp
void Update()
{
    if (_instances == null) return;
    HandleInput();
    HandleMovementInterrupt();
    UpdateCombatLocomotion();
    UpdateCombatLayerFadeOut();
    UpdateActionsSuppression();
    // 注意：此处不再有 SyncCombatFacing() 调用，也不重复写 IsStrafing。
}
```

#### 1.4 新增 `FixedUpdate()`（替代原 `SyncCombatFacing`）

```csharp
/// <summary>
/// 物理步内把当帧攻击朝向同步给 Motor 的 CombatFacing，供 Strafe 物理速度方向使用。
/// 背景(修复「实际位移方向不对」)：根朝向(LateUpdate)与位移方向(FixedUpdate)都读 CombatFacing，
/// 二者必须严格同源。若只在 Update 写 CombatFacing，本帧 FixedUpdate(先于 Update 执行)读到的是
/// 上一帧值——位移方向永远比根朝向慢一帧。故把「算攻击朝向 + 写 CombatFacing」整体移到 FixedUpdate：
/// SkillController 执行顺序 -10 先于 CharacterMotor(0)，本物理步先写好 CombatFacing，
/// 同一步内 CharacterMotor 用最新 CombatFacing 算速度；下一 LateUpdate 根也读同一 CombatFacing → 零相位差。
/// </summary>
void FixedUpdate()
{
    if (_instances == null) return;
    if (!_inCombatMode || _motor == null) return;

    bool isMoveAttack = enableAttackFacingHold && animPlayer != null && animPlayer.IsMoveAttack;
    bool isCasting = animPlayer != null && (animPlayer.IsCasting || animPlayer.IsFadingOut);
    if (isMoveAttack || !isCasting)
    {
        UpdateTargetDirection();
        _motor.CombatFacing = _targetDirection;
    }
}
```

#### 1.5 `LateUpdate()` 朝向锁定与实时跟随逻辑

```csharp
// ── 攻击朝向锁定：仅「静止普攻」期间(有起手朝向记录 _hasLastAttackFacing)，根节点钉在起手朝向 ──
// 移动攻击没有 _lastAttackFacing，不会被钉住，改由下方 ApplyCombatRotation 实时跟随鼠标。
bool attackPoseActive = enableAttackFacingHold && animPlayer != null && animPlayer.IsBasicAttackPose;
bool pinnedToAttackFacing = attackPoseActive && _hasLastAttackFacing && _lastAttackFacing.sqrMagnitude > 0.01f;
if (pinnedToAttackFacing)
{
    transform.rotation = Quaternion.LookRotation(_lastAttackFacing);
}

// 攻击姿态刚结束：解除朝向锁定
if (!attackPoseActive && _wasAttackPoseActive)
    _hasLastAttackFacing = false;
_wasAttackPoseActive = attackPoseActive;

// ── 战斗朝向：根实时面向鼠标(_targetDirection) ──
// 仅两种情况不施加：①静止普攻期间(已钉住)；②攻击后朝向保持期(_postAttackFacingHeld)。
if (_inCombatMode && !pinnedToAttackFacing && !_postAttackFacingHeld && _targetDirection.sqrMagnitude > 0.01f)
{
    ApplyCombatRotation();
}
```

> 需要新增 `private bool _wasAttackPoseActive;` 字段用于检测攻击姿态的边沿。

#### 1.6 `ApplyCombatRotation()` 优先读 `CombatFacing`（根与位移同源）

```csharp
private void ApplyCombatRotation()
{
    // 与物理位移方向严格同源：优先用 Motor.CombatFacing(FixedUpdate 里同步的攻击朝向)，
    // 回退 _targetDirection。消除根(渲染帧率采样)与位移(物理步采样)的时刻错位。
    Vector3 facing = _targetDirection;
    if (_motor != null && _motor.CombatFacing.sqrMagnitude > 0.01f)
        facing = _motor.CombatFacing;
    if (facing.sqrMagnitude < 0.01f) return;

    float h = _inputSource.MoveAxesRaw.x;
    float v = _inputSource.MoveAxesRaw.y;
    bool hasMovementInput = Mathf.Abs(h) > 0.05f || Mathf.Abs(v) > 0.05f;
    if (!hasMovementInput && !_isTurningOnSpot)
    {
        transform.rotation = Quaternion.LookRotation(facing);
        return;
    }

    if (_isTurningOnSpot)
    {
        Quaternion targetRot = Quaternion.LookRotation(facing);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRot,
            turnOnSpotRotationSpeed * Time.deltaTime);
    }
    else
    {
        // 快速面向攻击方向：移动中根瞬切对准鼠标，不做慢速弧线。
        transform.rotation = Quaternion.LookRotation(facing);
    }
}
```

#### 1.7 `UpdateTargetDirection()` 改用摄像机 `GetAimDirection`

```csharp
private void UpdateTargetDirection()
{
    var cam = _mainCamera != null ? _mainCamera : Camera.main;
    if (cam == null) return;

    // 攻击朝向用「硬贴摄像机位置」计算，不继承位置平滑(_followPosition 的 Lerp)。
    var tpc = cam.GetComponent<TopdownCameraController>();
    if (tpc != null)
    {
        Vector3 aim = tpc.GetAimDirection(transform.position, _inputSource.MousePosition);
        if (aim != Vector3.zero)
        {
            _targetDirection = aim;
            return;
        }
    }

    // 兜底(无 TopdownCameraController 时)：保持原地面射线逻辑。
    Ray ray = cam.ScreenPointToRay(_inputSource.MousePosition);
    Vector3 targetPoint;
    if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        targetPoint = hit.point;
    else
    {
        Plane groundPlane = new Plane(Vector3.up, transform.position);
        if (groundPlane.Raycast(ray, out float distance))
            targetPoint = ray.GetPoint(distance);
        else
            return;
    }

    Vector3 dir = targetPoint - transform.position;
    dir.y = 0f;
    if (dir.sqrMagnitude > 0.01f)
        _targetDirection = dir.normalized;
}
```

#### 1.8 `FaceMouseDirection()` 起手同步 `CombatFacing`

```csharp
private void FaceMouseDirection()
{
    UpdateTargetDirection();
    if (_targetDirection.sqrMagnitude > 0.01f)
    {
        transform.rotation = Quaternion.LookRotation(_targetDirection);
        // 起手当帧同步位移方向基准：攻击/技能起手前若 CombatFacing 残留旧值(如非战斗期间)，
        // 进入 Strafe 的第一帧物理步会用旧方向算位移 → 位移方向跳变。这里先钉住。
        if (_motor != null)
            _motor.CombatFacing = _targetDirection;
    }
}
```

#### 1.9 `TryBasicAttack()` 移动攻击分支起手同步 `CombatFacing`

```csharp
EnterCombatMode();
if (enableAttackFacingHold && isMovingAttack)
{
    UpdateTargetDirection();
    UnlockAttackFacing();
    // 起手当帧同步位移方向基准：消除「按住位移键触发攻击」时 CombatFacing 残留旧值
    // 导致的位移方向跳变(后续每物理步由 FixedUpdate 实时跟随鼠标)。
    if (_motor != null && _targetDirection.sqrMagnitude > 0.01f)
        _motor.CombatFacing = _targetDirection;
}
else
{
    FaceMouseDirection();
}

animPlayer.PlayRandomAttack(attackSlots, isMovingAttack);
...
// 静止普攻起手锁定朝向（在 !isMovingAttack 分支）
if (enableAttackFacingHold && !isMovingAttack)
{
    _lastAttackFacing    = _targetDirection;
    _hasLastAttackFacing = true;
    _postAttackFacingHeld = false;
}
```

#### 1.10 `ExitCombatMode()` 攻击后朝向保持前移落地

```csharp
public void ExitCombatMode()
{
    _inCombatMode = false;
    _movingAttackIntent = false;
    EndTurnOnSpot();

    // 攻击后朝向保持：攻击完全结束且无移动输入时退出战斗，角色保持当前(攻击)朝向，
    // 不被 FreeMovement/RotateWithCamera 立即拉回摄像机待机方向，直到下一次移动输入。
    // (此前该标志在 ExitCombatMode 之后才置位，而检查点又要求 _inCombatMode=true，导致死代码从未生效。)
    _postAttackFacingHeld = enableAttackFacingHold && !HasCurrentMovementInput();

    if (_motor != null)
    {
        _motor.locomotionType = _savedLocomotionType;
        _motor.isStrafing = false;
        _motor.lockRotation = _postAttackFacingHeld;   // 保持期仍锁定旋转
        (_motor as CharacterMotor)?.SetCombatMovementOverride(false);
    }
    ...
}
```

#### 1.11 `EnterCombatMode()` 传入移动攻击意图

```csharp
_inCombatMode = true;
...
(_motor as CharacterMotor)?.SetCombatMovementOverride(_movingAttackIntent);
```

---

### 2. `Assets/_Game/Scripts/Character/CharacterMotor.cs`

#### 2.1 新增 `CombatFacing` 字段（与 `isStrafing` 并列）

```csharp
public bool    isStrafing    { get; set; } = false;
public Vector3 CombatFacing  { get; set; } = Vector3.forward;
```

#### 2.2 Strafe 物理速度方向改用 `CombatFacing`（约 1713-1733 行）

```csharp
if (isStrafing)
{
    // 战斗 Strafe：直接硬切目标速度，任何方向切换(前后/左右/对角八向)都即时响应。
    // 不再走速度平滑——速度 Lerp 会让身体在旧方向上继续滑行一小段后才掉头。
    float slopeFactor = GetSlopeSpeedFactor();
    // 方向基准用 CombatFacing(当帧攻击朝向，SkillController 在物理步内先写入)，
    // 而非 transform.TransformDirection(依赖 transform.rotation，后者 LateUpdate 才瞬切)。
    // 这样物理速度方向与攻击朝向、动画方向参数三者同帧一致，方向切换零相位差。
    Vector3 fwd = CombatFacing;
    if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
    fwd.y = 0f;
    if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
    fwd.Normalize();
    Vector3 right = Vector3.Cross(Vector3.up, fwd);
    Vector3 v = (fwd * _speed + right * _direction)
        * (velocity > 0 ? velocity * slopeFactor : 1f);
    v.y = _rb.velocity.y;
    SetVelocity(v);
}
```

> 注意：`_speed` / `_direction` 需由 `StrafeLimitSpeed` 直读原始轴计算（见 2.3），二者相乘构成最终速度向量。

#### 2.3 `StrafeLimitSpeed` 直读 `MoveAxesRaw`（约 1559-1574 行）

```csharp
// MoveAxesRaw = GetAxisRaw 无平滑且当帧稳定，比 input 字段(上一帧 LateUpdate 写入)更新鲜。
Vector2 raw = _inputSource.MoveAxesRaw;
// 用 raw 计算 _speed / _direction，替代此前从 _motor.input 读取的逻辑
```

#### 2.4 `UpdateAnimator()` 直读原始轴写走位参数（约 2304-2335 行）

```csharp
bool movementUnlocked = !lockMovement || _combatMovementOverride;
bool canMove = !_stopMove && movementUnlocked;

float animatorVertical, animatorHorizontal;
if (canMove && isStrafing)
{
    // 走位方向参数直接驱动下半身 2D 八向融合树。当帧直读原始轴，绕开
    // 「LateUpdate 写 input → FixedUpdate 算 _speed/_direction → Update 写动画参数」的一帧延迟链。
    Vector2 raw = _inputSource.MoveAxesRaw;
    animatorVertical   = Mathf.Clamp(raw.y, -1f, 1f);
    animatorHorizontal = Mathf.Clamp(raw.x, -1f, 1f);
}
else if (canMove)
{
    animatorVertical   = _speed;
    animatorHorizontal = _direction;
}
else
{
    animatorVertical   = 0f;
    animatorHorizontal = 0f;
}
// 零阻尼即时写入(方向切换当帧生效)，停止时的柔和过渡由 InputMagnitude(0.2s 阻尼)承担。
```

> 注意：`_inputSource` 字段需已注入（见文件 5 的输入源）。若 test10 中 `CharacterMotor` 尚未持有 `_inputSource`，需补齐注入逻辑。

---

### 3. `Assets/_Game/Scripts/Character/TopdownCameraController.cs`

#### 3.1 新增目标角字段（供去平滑瞄准用，约 76/81 行）

```csharp
private float _targetYaw = 0f;      // 拖拽目标 yaw(度，平滑前)
private float _targetPitch = 60f;   // 拖拽目标俯仰角(度，平滑前)
```

> 这些字段在 `UpdateMouseDragRotate` 中更新（`_targetYaw -= dx*...`、`_targetPitch += dy*...`），`_yaw/_pitch` 则是 `SmoothDampAngle/SmoothDamp` 平滑后的值。瞄准需用未平滑的目标角。

#### 3.2 新增 `GetAimDirection()` + `ScreenPointToTargetDir()`

```csharp
public Vector3 GetAimDirection(Vector3 fromPosition, Vector2 screenPos)
{
    if (_cam == null) _cam = GetComponent<Camera>();
    if (_cam == null) return Vector3.forward;

    // 用「拖拽目标旋转」(未平滑的 _targetYaw/_targetPitch) 重建射线，摆脱两大滞后源：
    //   1) 位置平滑(_followPosition 的 Lerp)——改用硬贴摄像机位置；
    //   2) 旋转平滑(rotateSmoothTime=0.08 的 SmoothDamp)——改用目标角直算。
    Quaternion targetRot = Quaternion.Euler(_targetPitch, _targetYaw, 0f);

    // 硬贴摄像机位置(不含位置平滑)：角色 + targetOffset + 目标旋转球面偏移。
    Vector3 hardCamPos = (fromPosition + targetOffset) + targetRot * new Vector3(0f, 0f, -_currentDistance);

    // 射线方向：屏幕坐标 → NDC → 相机局部方向 → 目标旋转，等价于 ScreenPointToRay 但用目标角。
    Vector3 dir = ScreenPointToTargetDir(screenPos, targetRot);

    Plane plane = new Plane(Vector3.up, fromPosition);
    if (plane.Raycast(new Ray(hardCamPos, dir), out float dist))
    {
        Vector3 hit = hardCamPos + dir * dist;
        Vector3 d = hit - fromPosition;
        d.y = 0f;
        if (d.sqrMagnitude > 0.01f)
            return d.normalized;
    }
    return Vector3.zero;
}

/// <summary>用给定旋转(而非摄像机当前姿态)重建屏幕点射线方向。</summary>
private Vector3 ScreenPointToTargetDir(Vector2 screenPos, Quaternion targetRot)
{
    if (_cam == null) return Vector3.forward;

    float ndcX = (screenPos.x / Screen.width)  * 2f - 1f;
    float ndcY = (screenPos.y / Screen.height) * 2f - 1f;
    float halfH = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
    float halfW = halfH * _cam.aspect;

    Vector3 camLocal = new Vector3(ndcX * halfW, ndcY * halfH, 1f);
    return (targetRot * camLocal).normalized;
}
```

---

### 4. `Assets/_Game/Scripts/Character/CharacterInputHandler.cs`

#### 4.1 `MoveCharacter()` 战斗模式分流

```csharp
private void MoveCharacter()
{
    if (_isClickMoving)
    {
        float h = InputSource.MoveAxesRaw.x;
        float v = InputSource.MoveAxesRaw.y;
        if (Mathf.Abs(h) > 0.05f || Mathf.Abs(v) > 0.05f)
        {
            CancelClickMove();
            _motor.input = new Vector2(h, v);   // CancelClickMove 后立刻写入，不 return
        }
        return;
    }

    if (_skillController != null && _skillController.InCombatMode)
        RemapCombatInput();
    else
        ReadRawInput();
}

private void ReadRawInput()
{
    float h = InputSource.MoveAxesRaw.x;
    float v = InputSource.MoveAxesRaw.y;
    _motor.input = new Vector2(h, v);
}

private void RemapCombatInput()
{
    // 攻击朝向系移动(W跟枪口)：_motor.input 是机体坐标。
    //   input.y=+1 → 沿机体 forward(枪口/瞄准方向)前进 = 追击
    //   input.y=-1 → 后退(背离枪口) = 边退边打
    //   input.x=±1 → 侧移
    float dh = InputSource.MoveAxesRaw.x;
    float dv = InputSource.MoveAxesRaw.y;

    _motor.input = new Vector2(
        Mathf.Clamp(dh, -1f, 1f),
        Mathf.Clamp(dv, -1f, 1f));
}
```

---

### 5. `Assets/_Game/Scripts/Character/Input/CharacterInputSources.cs`

#### 5.1 接口声明（无平滑直读）

```csharp
Vector2 MoveAxesRaw { get; }    // GetAxisRaw("Horizontal"/"Vertical")，无平滑
Vector2 MousePosition { get; }
```

#### 5.2 实现

```csharp
public Vector2 MoveAxesRaw => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
public Vector2 MousePosition => Input.mousePosition;
```

---

### 6. `Assets/_Game/Scripts/Character/ICharacterMotor.cs`

#### 6.1 新增 `CombatFacing` 接口契约

```csharp
Vector3 CombatFacing { get; set; }
```

---

## 四、关键依赖与执行顺序（务必检查）

1. **脚本执行顺序**：`SkillController` 必须 `[DefaultExecutionOrder(-10)]`，`CharacterMotor` 保持默认 0。否则 `FixedUpdate` 里写 `CombatFacing` 与 Motor 算速度会错序，位移方向仍滞后。
2. **`CombatFacing` 接口链路**：`ICharacterMotor` 声明 → `CharacterMotor` 实现 → `SkillController` 写、`CharacterMotor.Strafe` 读、`SkillController.ApplyCombatRotation` 读。
3. **`MoveAxesRaw` 接口链路**：`IInputSource`（`CharacterInputSources`）声明 → `CharacterInputHandler`/`CharacterMotor`/`SkillController` 使用。若 test10 的 `CharacterMotor` 尚未持有 `_inputSource`，需先补齐注入。
4. **`GetAimDirection` 依赖**：`TopdownCameraController` 需已存在 `_targetYaw`/`_targetPitch`/`_currentDistance`/`targetOffset` 字段（若 test10 摄像机无拖拽旋转，需先补齐这些目标角字段及其在 `UpdateMouseDragRotate` 的更新）。
5. **数据流方向**：`GetAimDirection → _targetDirection → (CombatFacing + 根朝向 + 下半身动画)`，禁止任何中间环节再读平滑后的摄像机姿态。

---

## 五、验证清单（Play Mode）

1. 按住 W 走位、鼠标不动 → 攻击方向稳定，位移方向 = 攻击方向（追击）。
2. 移动中快速甩鼠标 → 根朝向与实际位移方向同向一致，不再「身体朝一个方向、人滑向另一方向」。
3. 越轴 + 八向输入 → 追击 / 边退边打 / 侧移即时切换，无滑行。
4. **非战斗状态按住 W 移动中点击普攻 → 攻击瞬间位移方向直接基于攻击朝向**，不再「先跳旧方向再跳回」。
5. 原地攻击后松手 → 角色不跳回待机方向（攻击后朝向保持）。
6. 快速连按攻击 + 甩鼠标多次 → 方向不随操作次数增加而累积滞后。

---

## 六、已知次要点（可暂不动）

- `TopdownCameraController` 的 `_targetYaw/_targetPitch` 在 `LateUpdate` 才更新，`FixedUpdate` 读到的是上一渲染帧值。**仅在右键拖拽旋转摄像机**时攻击朝向慢一帧（约 16ms）；纯鼠标移动瞄准这条主路径无此滞后。若 test10 验证时发现「拖拽旋转摄像机时位移仍慢一拍」，再单独把旋转目标角前移刷新。
