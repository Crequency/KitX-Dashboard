# 归档：面板宿主（Panel Host）

这里的文件**不参与编译，也不应该参与编译**。

## 为什么放在这里

它们是从 Dashboard 里移出来的旧面板宿主实现（约 2258 行）。移出的原因是
ToolKit 重构把「面板」从 ToolKit 的一等概念里删掉了（见 `Package/ToolKit-ADR-0013`），
Dashboard 不再有面板窗口。代码保留下来作为未来 View 层的参考，不是可复用实现。

## 一条必须遵守的约束

**这个目录不能移进 `KitX Dashboard/`（工程目录）。**

`.csproj` 在 `KitX Dashboard/KitX Dashboard/`，而本目录在它的**父目录**，所以 SDK 风格
项目的默认 glob 扫不到这里。这是它们不参与编译的唯一原因 —— 不是靠
`<Compile Remove>` 或 `<AvaloniaXaml Remove>` 排除的。

一旦有人把 `_archived/` 移进工程目录，或者给 csproj 加了指向父目录的 glob，
**这 9 个文件会一次性全部编译失败**，而且失败原因会很费解：

| 文件 | 会报什么 |
|---|---|
| `PanelHostWindow.axaml` | 绑定的 `PanelHostViewModel` 成员一个都不存在 |
| `PanelHostViewModel.cs` | 引用已删除的 `IPanelRuntime` / `UiPanel` / `UiControl` |
| `PanelControlViewModel.cs` | 同上 |
| `PanelControlTemplateSelector.cs` | 同上 |
| `PanelRunTimelineVMs.cs` | 引用已删除的 `InstanceStatus` |
| `PanelHostWindow.axaml.cs` | 引用已删除的 `PanelHostViewModel` |
| `PanelHostEventTests.cs` | 三个测试工程都看不见它们 |
| `PanelRunTimelineTests.cs` | 同上 |
| `PanelSelectOptionsTests.cs` | 同上 |

## 更要命的是 XAML 那一条

`KitX.Dashboard.csproj` 里有：

```xml
<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>
```

因为绑定**从不参与编译**，`PanelHostWindow.axaml` 里任何绑错的成员在编译期都不报错 ——
它们只会静默地什么都不显示。所以「编译过了」在这里完全不能作为「这个文件是好的」的证据。

（这也正是 S0 阶段把 `InstanceSnapshot.IsSilent` / `RunSummary` 删掉之后，编译零错误、
面板窗口空白了一个多小时的原因。见 `Package/dotnet-构建与测试场景实录.md`。）

## 如果将来要复活它

不要直接移回来。先把它依赖的契约（`IPanelRuntime`、`UiPanel`、`UiControl`、`InstanceStatus`）
按当前版本重新设计一遍 —— 这些类型在重构里是被**有意**删掉的，不是改名了。
