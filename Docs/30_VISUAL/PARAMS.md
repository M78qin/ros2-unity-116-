# 视觉参数（可直接复用/回滚）

全部由 `DigitalTwin/Doosan-DT/8 Apply Lighting Setup` 写入，可重复执行；改参数就改 `Assets/DoosanDT/10_DigitalTwin/Editor/DoosanDTPolish.cs` 里 `ApplyLighting()` / `ApplyProfile()` 的常量，再跑一次菜单。

## 1. 天空（`Assets/DoosanDT/10_DigitalTwin/Art/Materials/DoosanDT_IndustrialSky.mat`）

Shader `Skybox/Procedural`：

| 参数 | 值 | 意图 |
|---|---|---|
| `_SunDisk` | 1（HighQuality） | 有太阳盘，金属件有高光点 |
| `_SunSize` | 0.028 | 小太阳，避免"卡通大光斑" |
| `_SunSizeConvergence` | 2.2 | 边缘过渡 |
| `_AtmosphereThickness` | 0.85 | 介于清空与雾霾之间，车间窗户感 |
| `_SkyTint` | (0.56, 0.60, 0.66) | 冷灰蓝，工业冷调 |
| `_GroundColor` | (0.20, 0.19, 0.18) | 暖灰地面，避免底部发黑 |
| `_Exposure` | 1.15 | 略提亮天空，让窗外不过曝 |

RenderSettings：`ambientMode=Skybox`、`ambientIntensity=0.9`、`reflectionIntensity=0.85`、`defaultReflectionMode=Skybox`、`sun=主平行光`。

## 2. 灯光

| 灯 | 类型 | 位置/朝向 | 参数 | 说明 |
|---|---|---|---|---|
| Directional Light（主） | Directional | rot (50, −30, 0) | 色 (1.00, 0.96, 0.90)、强度 1.20、Soft 阴影 0.85、bias 0.05、normalBias 0.4、Mixed | 暖白顶光，程序化天空的太阳 |
| Directional Light（次） | Directional | 原朝向 | 强度 0.25、色 (0.88, 0.92, 1.00)、无阴影、Baked | 冷色补光，避免背光面死黑；烘焙后可关 |
| Fill_Light_A | Point | 环境包围盒 左上（top = maxY+0.6） | 色 (0.85,0.90,1.00)、强度 6、范围 9、无阴影、Baked | 顶部冷补光 |
| Fill_Light_B | Point | 环境包围盒 右下 | 同上 | 同上 |

`pixelLightCount=2`（Quality=High Fidelity），所以**除主光外最多 2 盏实时光**；其余必须 Baked，否则会吞掉主光/补光的实时光槽位。

## 3. 后处理（`Assets/DoosanDT/10_DigitalTwin/Art/Config/DoosanDT_VisualProfile.asset`）

挂到场景 `90_全局设置/Global Volume`（该 Volume 原已存在，本次替换其 profile；**不动** `Assets/Settings/SampleSceneProfile.asset`，那是其它场景共用的）。

| 组件 | 参数 |
|---|---|
| Tonemapping | ACES |
| Bloom | threshold 1.10、intensity 0.08、scatter 0.65、HighQualityFiltering on |
| ColorAdjustments | postExposure 0、contrast +8、saturation +5 |
| Vignette | intensity 0.12、smoothness 0.35 |

## 4. 管线（`Assets/Settings/URP-HighFidelity.asset`）

| 项 | 原值 | 现值 | 回滚 |
|---|---|---|---|
| `m_ColorGradingMode` | 0（LDR） | **1（HDR）** | 改回 0 |
| HDR / MSAA / 阴影 | 开 / 4x / 4096·4cascade·Soft | 不变 | — |

改 HDR 分级的原因：ACES + Bloom 在 LDR 下高光会被裁到 1.0，金属高光死白；HDR 下来源值可超过 1，Bloom 才有东西可"泛"。副作用：显存略增（每像素多 2 byte），VR 下可忽略。

> ⚠ **该资产被 git 忽略**（`.gitignore` 第 75 行 `/Assets/Settings/`），所以 HDR 分级改动只存在于本机磁盘，不在 git 里；换机/重新 clone 后重跑一次 `8 Apply Lighting Setup` 即可恢复。同理 `Assets/Scenes/` 也被忽略，场景全靠 `VERSION_LOG.md` + `_backup` 管理。

## 5. 静态标记

`40_环境与灯光` 下 227 个渲染器：`ContributeGI | BatchingStatic | OccluderStatic | OccludeeStatic | ReflectionProbeStatic`；该组 Collider 补 `BatchingStatic`。
注意：**机械臂和电池包不要打 ContributeGI**（它们是动态件，烘焙后会被"烙死"在光照贴图里）；本轮脚本只处理环境组，符合此约束。

## 6. 回滚方式（任选）

1. 场景级：用 `E:\new_2026-\unity\project\_backup\scenes\2026-11-03_pre_visual\` 下的备份 `.unity` + `.meta` 覆盖（SHA256 见 `VERSION_LOG.md`）。
2. 参数级：删 `DoosanDT_IndustrialSky.mat` / `DoosanDT_VisualProfile.asset`，把 URP `m_ColorGradingMode` 改回 0，Volume 换回 `SampleSceneProfile`。
3. 备份拆分回滚：把 `Doosan-DT_Archive.unity` 里的根拖回主场景（对象/组件都在，不丢引用，但会重新变成"大场景"）。