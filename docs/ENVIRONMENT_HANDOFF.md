# 任务 2 环境模块交接

## 交付范围

任务 2 负责现有咨询室的灯光、环境音和执行控制。任务 3 负责真实 LLM 的输入、情绪相关建议和输出解析。本模块提供从建议到用户确认、执行变化、状态回读的完整执行接口，保留原语音和沙盘逻辑。

工程基于仓库提交 `23abb6f8493073898507d1783535ea0fbf8cab8d`，使用 Unity 2022.3.62f3、URP 14.0.12、XR Interaction Toolkit 3.1.2。工作目录是 `G11_MindScape/MindScape`，本地分支为 `mode-b-environment`，未推送到远程。

## 打开和体验

1. 在 Unity Hub 中打开本文件所在仓库下的 `MindScape` 工程。
2. 点击顶部菜单 `MindScape > Open Environment Room`，打开实际的 BasicScene。初次打开工程出现的 Untitled 是默认空场景。
3. 按 Play。运行时自动生成 `Mode B Ambience`，无需手动挂脚本。
4. 未连接 XR 设备时，屏幕右上角显示桌面控制面板。VR 面板位于入口内侧，使用原工程的 XR UI 射线。手动预设按钮、亮度、音量、静音、恢复按钮均可直接操作。
5. 接受 AI 建议按钮初始禁用；任务 3 提交有效建议后启用。用户拒绝、恢复或手动调整偏好会清除待确认建议。

原工程的 Build Settings 默认启用 SampleScene。打包最终小组演示时，选择 BasicScene 作为启动场景。本模块没有自动覆盖组长的全局构建配置。

## 环境功能

| 预设 ID | 颜色近似 | 默认亮度倍率 | 声音 | 默认增益 |
|---|---|---|---|---|
| sadness | 2900 K 暖光 | 0.72 | SoftRain | 0.12 |
| anger | 3500 K 中性暖光 | 0.62 | QuietStream | 0.09 |
| calm | 3300 K 舒适暖光 | 1.00 | GentleAir | 0.12 |

数值是可以试用调整的 POC 初始偏好，不是临床处方或情绪识别结果。色温通过 RGB 近似实现，不代表头显实际输出的物理色温。软件音量增益不等于分贝。

* 灯光和环境音使用 2.5 秒平滑切换，按真实时间运行，暂停游戏时间不会冻结过渡。
* 每段音源有独立播放器，连续切换不会截断正在淡出的音源；稳定后其余音源停止。
* 不加入闪烁、呼吸明暗或自动强制转为欢快氛围。
* 用户亮度范围为初始强度的 0.45–1.25 倍。用户手动调亮或调暗后，该偏好会跨预设保留，直到恢复原状。
* 音量偏好范围为 0–1，乘在预设默认增益上。静音和音量偏好跨预设保留。
* 恢复原状会渐变回模块初始化时的灯光并淡出环境音，同时保留用户的静音和音量偏好。
* 停用组件时立即恢复初始灯光并停止声音，清除待确认建议，状态回读同步复位。
* 原落地灯开关通过统一环境控制器调光，避免两个脚本争抢同一盏灯。

三个 WAV 为固定种子、周期性频域滤波生成的原创合成环境纹理，不是真实雨声或流水现场录音。每段 24 秒、22,050 Hz、16 位单声道，采样峰值约 0.24，不含人声或鼓点。可用 `tools/generate_ambience.py` 重现生成。实际舒适性仍需用户在目标设备试听。

## 任务 3 接入

所有调用放在 Unity 主线程。模块不包含 API Key，也不发起模型请求。

```csharp
using MindScape.Environment;

var environment = AmbienceController.Instance;
if (environment == null) return;
var bridge = environment.GetComponent<AmbienceCommandBridge>();

// 先把当前状态放入你真正发送给 LLM 的上下文。
string environmentContext = bridge.GetContextJson();

// 你的 LLM 应返回执行协议，例如：
// {"action":"propose_ambience","preset":"anger"}
// 将真实模型输出传入，不要把下面的示例作为实际模型响应。
bool accepted = bridge.TryProposeJson(actualModelOutput, "turn-002");

// 用户在面板接受后执行。你也可从自己的确认 UI 调用：
// environment.ConfirmProposal("turn-002");
// 拒绝则调用 environment.RejectProposal();

environment.StateChanged += state => {
    // 保存执行结果，作为下一次真实模型请求的环境上下文。
};
```

接口只允许 `propose_ambience` 动作和 sadness、anger、calm 三个预设。无效 JSON、其他动作、未知预设、空或过长请求 ID 都会被拒绝。成功返回 true 仅表示进入待确认状态，不表示已经执行。带请求 ID 的确认可拒绝旧建议的过期确认。

直接接口 `ProposePreset(id, requestId)` 仍可用。`ApplyManual(id)` 只用于用户手动操作或测试。模型请求、原始上下文和原始模型响应由任务 3 自己记录，本模块日志不能替代它们。

状态字段说明：

* `preset` 是当前目标，`completedPreset` 是最近完成的目标；过渡中二者可能不同。
* `pendingPreset`、`pendingRequestId` 是待确认建议。
* `brightness`、`volume`、`kelvin` 是目标值；`lights` 数组保存读取时各灯的实际强度与颜色。
* `effectiveVolume` 是当前未静音、正在播放的环境音源增益总和，不是声压测量值。
* `brightnessOverridden`、`soundLevel`、`muted` 表示用户偏好。
* `source` 标记执行路径，`llm-confirmed` 本身不证明真实调用过模型；自动测试以 `fixture-` 请求 ID 明确标记测试输入。

原项目语音对话仍使用原有服务。环境音可以静音，但尚未加入自动压低对话背景音的功能。

## 验证与证据

运行记录、完整结果和房间截图见 `docs/verification`。详细结果以其中最终时间戳和源码哈希为准。

已执行的验证包括：

1. 编译全部五个环境运行脚本，使用本机 Unity 的真实引擎和 UI 引用。
2. 对控制器实际使用的 AmbienceSession 执行 22 项离线检查，覆盖无效输入、用户确认、过期确认、偏好保留、范围限制和恢复。
3. 在 Windows 的 Unity 2022.3.62f3 中运行六项 Play Mode 测试：确认与渐变、快速切换声音、偏好与停用、JSON 指令边界、桌面 UI 射线与按钮、实际 BasicScene 双情境测试。
4. 实际房间测试调用模块的指令接口，执行悲伤、愤怒及恢复路径，记录各灯实际值，并通过 URP 摄像机渲染保存截图。截图展示环境，不包含屏幕空间桌面面板。

重要边界：自动测试使用预设 JSON fixture，证明环境执行接口可以工作，不代表与任务 3 的真实 LLM 请求已完成联调。尚未连接头显完成实际控制器操作与主观听感验收，也未验证治疗效果。

重跑方式：

* Unity 菜单 `MindScape > Run Environment Play Mode Tests`。测试前保存当前场景。
* PowerShell 运行 `tools/verify_environment.ps1`，可重跑全部环境运行脚本的独立编译与 22 项离线检查。实际 Unity 安装路径不同时使用 `-UnityEditor` 参数指定。
* Play Mode 的最新机器结果位于 `MindScape/Library/MindScapeEnvironment`，由测试脚本自动写入，交付证据复制到 `docs/verification`。

## 对应老师要求

更新版课程要求第 2、6、7、8 节强调场景上下文、模型输出影响虚拟世界、更新状态进入后续交互。环境模块承担执行与状态反馈职责，不需要复杂画面。

11 月 27 日 23:59 HKT 前提交约五页 PDF 报告和源码 ZIP，报告中附 5–7 分钟 YouTube Unlisted 视频链接。视频至少展示两个情境，且要显式展示真实模型输入、输出及其效果。新版不另交 PPT。

建议小组录制：

1. 用户低落，真实模型读取 baseline 和用户偏好，提出 sadness；用户确认后环境改变，用户反馈太暗并调亮，下一轮模型上下文包含更新状态。
2. 用户激动且希望安静，真实模型读取静音和用户反馈，提出 anger；确认后保持静音，灯光平稳改变；用户可拒绝后续建议或恢复。

请把自动测试中的 fixture 与小组录制的真实模型输出分开说明。

## 个人贡献表述

“我负责咨询室氛围的执行层，实现了三种灯光和环境音预设、平滑切换、用户确认、亮度音量偏好及恢复功能。我提供 JSON 指令入口和状态回读，供 AI 环境调节模块接入，并完成状态逻辑检查和 Unity Play Mode 测试。”

这是实现与测试贡献的表述，不应改写成“已证明改善心理健康”或“已完成真实 LLM 联调”。
