# 视觉与性能基线（可比较的数字表）

采集方式：编辑器菜单 `DigitalTwin/Doosan-DT/7 Scene Stats` → `Logs/doosan_dt_polish.txt`。
口径：编辑器内 `FindObjectsOfType<T>(true)`（含未激活、含已加载其它场景），非运行时采样、非 GPU 计时。

## 1. 主场景 `Doosan-DT.unity`：整理前 / 后

| 指标 | 前 20:07:55 | 后 20:19:37 | 变化 |
|---|---|---|---|
| Transform 对象 | 6,154 | 1,476 | −76.0% |
| 根节点数 | 9 | 7 | −2 |
| MeshRenderer 总数 | 3,070 | 749 | −75.6% |
| 其中激活 | 282 | 282 | 0 |
| 三角面（总） | 11,823,679 | 2,884,871 | −75.6% |
| 三角面（激活） | 984,136 | 984,136 | **0** |
| 独立 Mesh | 191 | 191 | 0 |
| 独立材质 | 35 | 35 | 0 |
| Outline 组件（启用） | 2,153（38） | 162（5） | −92.5%（−86.8%） |
| 相机（活） | 3（1） | 3（1） | 0 |
| 灯光（活） | 4（2） | 6（4） | 补光 +2 |
| Volume | 1 | 1 | 0 |
| Reflection Probe | 0 | 0 | 0 |
| Light Probe Group | 0 | 0 | 0 |
| 贴图张数（Assets 内） | 127 | 127 | 0 |
| 贴图估算显存 | 221.9 MB | 221.9 MB | 0 |
| **场景文件** | **12.17 MB**（+静态标记 13.17） | **3.26 MB** | −73.2% |
| 缺失脚本实例 | 3 | 2 | −1（随备份归档） |

备份场景 `Doosan-DT_Archive.unity` = 9.91 MB（承接 4,683 个对象：`~` 3,962 + 2 个 `-bkup113` 组）。

## 2. 主场景根节点构成（后）

| 根 | Transform | 激活 | 渲染器 |
|---|---|---|---|
| 00_数字孪生机械臂 | 619 | 235 | 99 |
| 20_漫游与训练 | 391 | 97 | 89 |
| 30_界面与交互 | 86 | 5 | 0 |
| 40_环境与灯光 | 307 | 145 | 94 |
| 50_玩家与相机 | 67 | 3 | 0 |
| 90_全局设置 | 4 | 4 | 0 |
| ~_外部引用保留（停用） | 2 | 1 | 0 |

## 3. 怎么读这组数字（防止误用）

- **能被论文当"减负证据"的**：对象数 −76%、场景体积 −73%、Outline 组件 −92%、备份隔离到独立场景（打开主场景不再解析 3,962 个历史对象）。
- **不能说的**：不能说"三角面降了 76%"。激活三角面完全没变，GPU 负载没变；总三角面下降只是因为备份树搬出了场景。
- **还没测的**：真实帧时间 / 帧率 / draw call / GC。要测必须进 Play（只读采样），需用户单独授权（`TASKS.md` C2）。

## 4. 截图证据（`Logs/`）

| 文件 | 内容 | 尺寸 |
|---|---|---|
| `doosan_dt_view_robot_before.png` | 装灯前 · 机械臂+电池包近景 | 1920×1080 |
| `doosan_dt_view_robot_after.png` | 装灯后 · 同机位 | 1920×1080 |
| `doosan_dt_view_workcell_before.png` | 装灯前 · 工作站中景 | 1920×1080 |
| `doosan_dt_view_workcell_after.png` | 装灯后 · 同机位 | 1920×1080 |
| `doosan_dt_view_wide_before.png` | 装灯前 · 全景 | 1920×1080 |
| `doosan_dt_view_wide_after.png` | 装灯后 · 同机位 | 1920×1080 |

机位由脚本按激活渲染器包围盒推导，故"前后"严格同机位；`_after` 文件体积明显小于 `_before`（119 KB vs 285 KB 等），与"天空盒+ACES 取代纯色背景、画面噪声减少"一致，**但这不能替代人眼判断**，观感需用户确认（偏亮/偏暗/过饱和）。

## 5. 复现命令

```
菜单：DigitalTwin/Doosan-DT/7 Scene Stats          → 追加统计到 Logs/doosan_dt_polish.txt
菜单：DigitalTwin/Doosan-DT/2 Verify Merged Scene  → Logs/doosan_dt_verify.txt
菜单：DigitalTwin/Doosan-DT/9 Snapshot Views       → Logs/doosan_dt_view_*_{before|after}.png
```
（MCP 调用示例见 `E:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-03_fusion\README.md`）