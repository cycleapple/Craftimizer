using Craftimizer.Simulator;
using Craftimizer.Simulator.Actions;
using Craftimizer.Solver;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Craftimizer.Plugin.Windows;

public sealed class Settings : Window, IDisposable
{
    private const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.None;

    private static Configuration Config => Service.Configuration;

    private static float OptionWidth => 200 * ImGuiHelpers.GlobalScale;
    private static Vector2 OptionButtonSize => new(OptionWidth, ImGui.GetFrameHeight());

    private string? SelectedTab { get; set; }

    private IFontHandle HeaderFont { get; }
    private IFontHandle SubheaderFont { get; }

    public Settings() : base("Craftimizer 設定", WindowFlags)
    {
        Service.WindowSystem.AddWindow(this);

        HeaderFont = Service.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(UiBuilder.DefaultFontSizePx * 2f)));
        SubheaderFont = Service.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(UiBuilder.DefaultFontSizePx * 1.5f)));

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new(450, 400),
            MaximumSize = new(float.PositiveInfinity)
        };
    }

    public void SelectTab(string label)
    {
        SelectedTab = label;
    }

    private ImRaii.IEndObject TabItem(string label)
    {
        var idSeparator = label.IndexOf("###", StringComparison.Ordinal);
        var selectionKey = idSeparator >= 0 ? label[(idSeparator + 3)..] : label;
        var isSelected = string.Equals(SelectedTab, selectionKey, StringComparison.Ordinal);
        if (isSelected)
        {
            SelectedTab = null;
            var open = true;
            return ImRaii.TabItem(label, ref open, ImGuiTabItemFlags.SetSelected);
        }
        return ImRaii.TabItem(label);
    }

    private static void DrawOption(string label, string tooltip, bool val, Action<bool> setter, ref bool isDirty)
    {
        if (ImGui.Checkbox(label, ref val))
        {
            setter(val);
            isDirty = true;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiUtils.TooltipWrapped(tooltip);
    }

    private static void DrawOption<T>(string label, string tooltip, T value, T min, T max, Action<T> setter, ref bool isDirty) where T : struct, INumber<T>
    {
        ImGui.SetNextItemWidth(OptionWidth);
        var text = value.ToString();
        ArgumentNullException.ThrowIfNull(text, nameof(value));
        if (ImGui.InputText(label, ref text, 8, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.CharsDecimal))
        {
            if (T.TryParse(text, null, out var newValue))
            {
                newValue = T.Clamp(newValue, min, max);
                if (value != newValue)
                {
                    setter(newValue);
                    isDirty = true;
                }
            }
        }
        else
        {
            var newValue = T.Clamp(value, min, max);
            if (value != newValue)
            {
                setter(newValue);
                isDirty = true;
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiUtils.TooltipWrapped(tooltip);
    }

    private static void DrawOption(string label, string tooltip, string value, Action<string> setter, ref bool isDirty)
    {
        ImGui.SetNextItemWidth(OptionWidth);
        var text = value;
        if (ImGui.InputText(label, ref text, 255, ImGuiInputTextFlags.AutoSelectAll))
        {
            if (!string.Equals(value, text, StringComparison.Ordinal))
            {
                setter(text);
                isDirty = true;
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiUtils.TooltipWrapped(tooltip);
    }

    private static void DrawOption<T>(string label, string tooltip, Func<T, string> getName, Func<T, string> getTooltip, T value, Action<T> setter, ref bool isDirty, params T[] excludedValues) where T : struct, Enum
    {
        ImGui.SetNextItemWidth(OptionWidth);
        using (var combo = ImRaii.Combo(label, getName(value)))
        {
            if (combo)
            {
                foreach (var type in Enum.GetValues<T>())
                {
                    if (excludedValues.Contains(type))
                        continue;
                    if (ImGui.Selectable(getName(type), value.Equals(type)))
                    {
                        setter(type);
                        isDirty = true;
                    }
                    if (ImGui.IsItemHovered())
                        ImGuiUtils.TooltipWrapped(getTooltip(type));
                }
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiUtils.TooltipWrapped(tooltip);
    }

    private static string GetAlgorithmName(SolverAlgorithm algorithm) =>
        algorithm switch
        {
            SolverAlgorithm.Oneshot => "單次",
            SolverAlgorithm.OneshotForked => "多執行緒單次",
            SolverAlgorithm.Stepwise => "逐步",
            SolverAlgorithm.StepwiseForked => "多執行緒逐步",
            SolverAlgorithm.StepwiseGenetic => "逐步遺傳",
            SolverAlgorithm.Raphael => "最佳化",
            _ => "未知",
        };

    private static string GetAlgorithmTooltip(SolverAlgorithm algorithm) =>
        algorithm switch
        {
            SolverAlgorithm.Oneshot =>          "執行所有迭代並選出最佳巨集",
            SolverAlgorithm.OneshotForked =>    "同時使用多個求解器進行單次求解",
            SolverAlgorithm.Stepwise =>         "執行所有迭代並選出下一個最佳步驟，" +
                                                "再以前面的步驟為起點重複求解",
            SolverAlgorithm.StepwiseForked =>   "同時使用多個求解器進行逐步求解",
            SolverAlgorithm.StepwiseGenetic =>  "從各求解器選出前 N 個最佳步驟，" +
                                                "並將每個步驟同等作為下一輪起點",
            SolverAlgorithm.Raphael =>          "以不同演算法穩定尋找最佳解，" +
                                                "因此使用的選項也與其他求解器不同。",
            _ => "未知"
        };

    private static string GetCopyTypeName(MacroCopyConfiguration.CopyType type) =>
        type switch
        {
            MacroCopyConfiguration.CopyType.OpenWindow => "開啟視窗",
            MacroCopyConfiguration.CopyType.CopyToMacro => "複製至遊戲巨集",
            MacroCopyConfiguration.CopyType.CopyToClipboard => "複製到剪貼簿",
            MacroCopyConfiguration.CopyType.CopyToMacroMate => "複製至 Macro Mate",
            _ => "未知",
        };

    private static string GetCopyTypeTooltip(MacroCopyConfiguration.CopyType type) =>
        type switch
        {
            MacroCopyConfiguration.CopyType.OpenWindow =>       "開啟專用視窗顯示所有巨集，供你檢視與選擇複製。",
            MacroCopyConfiguration.CopyType.CopyToMacro =>      "直接複製到遊戲的巨集系統。",
            MacroCopyConfiguration.CopyType.CopyToClipboard =>  "複製到剪貼簿；巨集之間以空白行分隔。",
            MacroCopyConfiguration.CopyType.CopyToMacroMate =>  "直接複製到 Macro Mate 巨集；需要安裝 Macro Mate。",
            _ => "未知"
        };

    private static string GetProgressBarTypeName(Configuration.ProgressBarType type) =>
        type switch
        {
            Configuration.ProgressBarType.Colorful => "彩色",
            Configuration.ProgressBarType.Simple => "簡潔",
            Configuration.ProgressBarType.None => "無",
            _ => "未知",
        };

    private static string GetProgressBarTooltip(Configuration.ProgressBarType type) =>
        type switch
        {
            Configuration.ProgressBarType.Colorful => "使用彩虹色彩",
            Configuration.ProgressBarType.Simple => "使用灰階色彩",
            Configuration.ProgressBarType.None => "不顯示進度條，只顯示完成百分比",
            _ => "未知"
        };

    public override void Draw()
    {
        if (ImGui.BeginTabBar("settingsTabBar"))
        {
            DrawTabGeneral();
            DrawTabRecipeNote();
            if (Config.EnableSynthHelper)
                DrawTabSynthHelper();
            DrawTabMacroEditor();
            DrawTabAbout();

            ImGui.EndTabBar();
        }
    }

    private void DrawTabGeneral()
    {
        using var tab = TabItem("一般###General");
        if (!tab)
            return;

        ImGuiHelpers.ScaledDummy(5);

        var isDirty = false;

        DrawOption(
            "啟用製作輔助",
            "Adds a helper next to your synthesis window to help solve for the best craft. " +
            "Extremely useful for expert recipes, where the condition can greatly affect " +
            "which actions you take.",
            Config.EnableSynthHelper,
            v => Config.EnableSynthHelper = v,
            ref isDirty
        );

        DrawOption(
            "製作筆記僅顯示一項巨集數值",
            "Only one stat will be shown for a macro. If a craft will be finished, quality " +
            "is shown. Otherwise, progress is shown. Durability and remaining CP will be " +
            "hidden.",
            Config.ShowOptimalMacroStat,
            v => Config.ShowOptimalMacroStat = v,
            ref isDirty
        );

        DrawOption(
            "檢查設計圖",
            "Your inventory will be checked to ensure that you have delineations available " +
            "before suggesting any specialist actions.",
            Config.CheckDelineations,
            v => Config.CheckDelineations = v,
            ref isDirty
        );

        DrawOption(
            "可靠度測試次數",
            "When testing for reliability of a macro in the editor, this many trials will be " +
            "run. You should set this value to at least 100 to get a reliable spread of data. " +
            "If it's too low, you may not find an outlier, and the average might be skewed.",
            Config.ReliabilitySimulationCount,
            5,
            5000,
            v => Config.ReliabilitySimulationCount = v,
            ref isDirty
        );

        DrawOption(
            "進度條樣式",
            "The style of progress bar to use when solving for a macro.",
            GetProgressBarTypeName,
            GetProgressBarTooltip,
            Config.ProgressType,
            v => Config.ProgressType = v,
            ref isDirty
        );

        ImGuiHelpers.ScaledDummy(5);

        using (var panel = ImRaii2.GroupPanel("複製設定", -1, out _))
        {
            DrawOption(
                "巨集複製方式",
                "The method to copy a macro with.",
                GetCopyTypeName,
                GetCopyTypeTooltip,
                Config.MacroCopy.Type,
                v => Config.MacroCopy.Type = v,
                ref isDirty
            );

            if (Config.MacroCopy.Type == MacroCopyConfiguration.CopyType.CopyToMacroMate &&
                !Service.PluginInterface.InstalledPlugins.Any(p => p.IsLoaded && string.Equals(p.InternalName, "MacroMate", StringComparison.Ordinal)))
            {
                ImGui.SameLine();
                using (var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange))
                {
                    using var font = ImRaii.PushFont(UiBuilder.IconFont);
                    ImGui.TextUnformatted(FontAwesomeIcon.ExclamationCircle.ToIconString());
                }
                if (ImGui.IsItemHovered())
                    ImGuiUtils.Tooltip("尚未安裝 Macro Mate");
            }

            if (Config.MacroCopy.Type == MacroCopyConfiguration.CopyType.CopyToMacro)
            {
                DrawOption(
                    "向下複製",
                    "Copy subsequent macros downward (#1 -> #11) instead of to the right.",
                    Config.MacroCopy.CopyDown,
                    v => Config.MacroCopy.CopyDown = v,
                    ref isDirty
                );

                DrawOption(
                    "複製至共用巨集",
                    "Copy to the shared macros tab. Leaving this unchecked copies to the " +
                    "individual tab.",
                    Config.MacroCopy.SharedMacro,
                    v => Config.MacroCopy.SharedMacro = v,
                    ref isDirty
                );

                DrawOption(
                    "巨集編號",
                    "The # of the macro to being copying to. Subsequent macros will be " +
                    "copied relative to this macro.",
                    Config.MacroCopy.StartMacroIdx,
                    0, 99,
                    v => Config.MacroCopy.StartMacroIdx = v,
                    ref isDirty
                );

                DrawOption(
                    "巨集複製數量上限",
                    "The maximum number of macros to be copied. Any more and a window is " +
                    "displayed with the rest of them.",
                    Config.MacroCopy.MaxMacroCount,
                    1, 99,
                    v => Config.MacroCopy.MaxMacroCount = v,
                    ref isDirty
                );
            }
            else if (Config.MacroCopy.Type == MacroCopyConfiguration.CopyType.CopyToMacroMate)
            {
                DrawOption(
                    "巨集名稱",
                    "The name of the macro to be created or updated in Macro Mate.",
                    Config.MacroCopy.MacroMateName,
                    v => Config.MacroCopy.MacroMateName = v,
                    ref isDirty
                );

                DrawOption(
                    "巨集上層群組",
                    "The name of the parent group of the new macro. Leave blank or \"/\" if there is none.",
                    Config.MacroCopy.MacroMateParent,
                    v => Config.MacroCopy.MacroMateParent = v,
                    ref isDirty
                );
            }

            DrawOption(
                "顯示複製完成訊息",
                "Shows a notification in the bottom right when a macro is copied successfully.",
                Config.MacroCopy.ShowCopiedMessage,
                v => Config.MacroCopy.ShowCopiedMessage = v,
                ref isDirty
            );

            if (Config.MacroCopy.Type != MacroCopyConfiguration.CopyType.CopyToMacroMate)
            {
                DrawOption(
                    "使用 Macro Chain",
                    "Replaces the last step with /nextmacro to run the next macro " +
                    "automatically. Overrides the Intermediate Notification Sound.",
                    Config.MacroCopy.UseNextMacro,
                    v => Config.MacroCopy.UseNextMacro = v,
                    ref isDirty
                );

                if (Config.MacroCopy.UseNextMacro &&
                    !Service.PluginInterface.InstalledPlugins.Any(p => p.IsLoaded && string.Equals(p.InternalName, "MacroChain", StringComparison.Ordinal)))
                {
                    ImGui.SameLine();
                    using (var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange))
                    {
                        using var font = ImRaii.PushFont(UiBuilder.IconFont);
                        ImGui.TextUnformatted(FontAwesomeIcon.ExclamationCircle.ToIconString());
                    }
                    if (ImGui.IsItemHovered())
                        ImGuiUtils.Tooltip("尚未安裝 Macro Chain");
                }
            }

            DrawOption(
                "加入巨集鎖定",
                "Adds /mlock to the beginning of every macro. Prevents other " +
                "macros from being run.",
                Config.MacroCopy.UseMacroLock,
                v => Config.MacroCopy.UseMacroLock = v,
                ref isDirty
            );

            DrawOption(
                "加入通知",
                "Replaces the last step of every macro with a /echo notification.",
                Config.MacroCopy.AddNotification,
                v => Config.MacroCopy.AddNotification = v,
                ref isDirty
            );

            if (Config.MacroCopy.AddNotification)
            {
                if ((Config.MacroCopy.Type == MacroCopyConfiguration.CopyType.CopyToMacro || !Config.MacroCopy.CombineMacro) && Config.MacroCopy.Type != MacroCopyConfiguration.CopyType.CopyToMacroMate)
                {
                    DrawOption(
                        "強制加入通知",
                        "Prioritize always having a notification sound at the end of " +
                        "every macro. Keeping this off prevents macros with only 1 action.",
                        Config.MacroCopy.ForceNotification,
                        v => Config.MacroCopy.ForceNotification = v,
                        ref isDirty
                    );
                }

                DrawOption(
                    "加入通知音效",
                    "Adds a sound to the end of every macro.",
                    Config.MacroCopy.AddNotificationSound,
                    v => Config.MacroCopy.AddNotificationSound = v,
                    ref isDirty
                );

                if (Config.MacroCopy.AddNotificationSound)
                {
                    if (!Config.MacroCopy.UseNextMacro && Config.MacroCopy.Type != MacroCopyConfiguration.CopyType.CopyToMacroMate)
                    {
                        DrawOption(
                            "中間巨集通知音效",
                            "Ending notification sound for an intermediary macro.\n" +
                            "Uses <se.#>",
                            Config.MacroCopy.IntermediateNotificationSound,
                            1, 16,
                            v =>
                            {
                                Config.MacroCopy.IntermediateNotificationSound = v;
                                UIGlobals.PlayChatSoundEffect((uint)v);
                            },
                            ref isDirty
                        );
                    }

                    DrawOption(
                        "最終巨集通知音效",
                        "Ending notification sound for the final macro.\n" +
                        "Uses <se.#>",
                        Config.MacroCopy.EndNotificationSound,
                        1, 16,
                        v =>
                        {
                            Config.MacroCopy.EndNotificationSound = v;
                            UIGlobals.PlayChatSoundEffect((uint)v);
                        },
                        ref isDirty
                    );
                }
            }

            if (Config.MacroCopy.Type != MacroCopyConfiguration.CopyType.CopyToMacro)
            {
                DrawOption(
                    "移除等待時間",
                    "Remove <wait.#> at the end of every action.",
                    Config.MacroCopy.RemoveWaitTimes,
                    v => Config.MacroCopy.RemoveWaitTimes = v,
                    ref isDirty
                );

                if (Config.MacroCopy.Type != MacroCopyConfiguration.CopyType.CopyToMacroMate)
                {
                    DrawOption(
                    "合併巨集",
                        "Doesn't split the macro into smaller macros.",
                        Config.MacroCopy.CombineMacro,
                        v => Config.MacroCopy.CombineMacro = v,
                        ref isDirty
                    );
                }
            }
        }

        if (isDirty)
            Config.Save();
    }

    private static void DrawSolverConfig(ref SolverConfig configRef, SolverConfig defaultConfig, bool disableOptimal, out bool isDirty)
    {
        isDirty = false;

        var config = configRef;

        using (var panel = ImRaii2.GroupPanel("一般", -1, out _))
        {
            if (ImGui.Button("重設為預設值", OptionButtonSize))
            {
                config = defaultConfig;
                isDirty = true;
            }

            DrawOption(
                "演算法",
                "The algorithm to use when solving for a macro. Different " +
                "algorithms provide different pros and cons for using them. " +
                "By far, the Optimal and Stepwise Genetic algorithms provide " +
                "the best results, especially for very difficult crafts.",
                GetAlgorithmName,
                GetAlgorithmTooltip,
                config.Algorithm,
                v => config = config with { Algorithm = v },
                ref isDirty,
                disableOptimal ? [SolverAlgorithm.Raphael] : []
            );

            using (ImRaii.Disabled(config.Algorithm is not (SolverAlgorithm.OneshotForked or SolverAlgorithm.StepwiseForked or SolverAlgorithm.StepwiseGenetic or SolverAlgorithm.Raphael)))
                DrawOption(
                "核心數量上限",
                    "The number of cores to use when solving. You should use as many " +
                    "as you can. If it's too high, it will have an effect on your gameplay " +
                    $"experience. A good estimate would be 1 or 2 cores less than your " +
                    $"system (FYI, you have {Environment.ProcessorCount} cores), but make sure to accomodate " +
                    $"for any other tasks you have in the background, if you have any.\n" +
                    "(Only used in the Forked, Genetic, and Optimal algorithms)",
                    config.MaxThreadCount,
                    1,
                    Environment.ProcessorCount,
                    v => config = config with { MaxThreadCount = v },
                    ref isDirty
                );

            if (config.Algorithm != SolverAlgorithm.Raphael)
            {
                DrawOption(
                "目標迭代次數",
                    "The total number of iterations to run per crafting step. " +
                    "Higher values require more computational power. Higher values " +
                    "also may decrease variance, so other values should be tweaked " +
                    "as necessary to get a more favorable outcome.",
                    config.Iterations,
                    1000,
                    1000000,
                    v => config = config with { Iterations = v },
                    ref isDirty
                );

                DrawOption(
                "迭代次數上限",
                    "The solver may go about the target iteration value if the craft " +
                    "is sufficiently difficult, and it wasn't able to find any way to " +
                    "complete it yet. In rare cases, the solver might go on for a very " +
                    "long time. This maximum is here to prevent the solver from stealing " +
                    "all your RAM.",
                    config.MaxIterations,
                    config.Iterations,
                    5000000,
                    v => config = config with { MaxIterations = v },
                    ref isDirty
                );

                DrawOption(
                "步驟數量上限",
                    "The maximum number of crafting steps; this is generally the only " +
                    "setting you should change, and it should be set to around 5 steps " +
                    "more than what you'd expect. If this value is too low, the solver " +
                    "won't learn much per iteration; too high and it will waste time " +
                    "on useless extra steps.",
                    config.MaxStepCount,
                    1,
                    100,
                    v => config = config with { MaxStepCount = v },
                    ref isDirty
                );

                DrawOption(
                "探索常數",
                    "A constant that decides how often the solver will explore new, " +
                    "possibly good paths. If this value is too high, " +
                    "moves will mostly be decided at random.",
                    config.ExplorationConstant,
                    0,
                    10,
                    v => config = config with { ExplorationConstant = v },
                    ref isDirty
                );

                DrawOption(
                "分數權重常數",
                    "A constant ranging from 0 to 1 that configures how the solver " +
                    "scores and picks paths to travel to next. A value of 0 means " +
                    "actions will be chosen based on their average outcome, whereas " +
                    "1 uses their best outcome achieved so far.",
                    config.MaxScoreWeightingConstant,
                    0,
                    1,
                    v => config = config with { MaxScoreWeightingConstant = v },
                    ref isDirty
                );

                using (ImRaii.Disabled(config.Algorithm is not (SolverAlgorithm.OneshotForked or SolverAlgorithm.StepwiseForked or SolverAlgorithm.StepwiseGenetic)))
                    DrawOption(
                "分支數量",
                        "Split the number of iterations across different solvers. In general, " +
                        "you should increase this value to at least the number of cores in " +
                        $"your system (FYI, you have {Environment.ProcessorCount} cores) to attain the most speedup. " +
                        "The higher the number, the more chance you have of finding a " +
                        "better local maximum; this concept similar but not equivalent " +
                        "to the exploration constant.\n" +
                        "(Only used in the Forked and Genetic algorithms)",
                        config.ForkCount,
                        1,
                        500,
                        v => config = config with { ForkCount = v },
                        ref isDirty
                    );

                using (ImRaii.Disabled(config.Algorithm is not SolverAlgorithm.StepwiseGenetic))
                    DrawOption(
                "菁英技能數量",
                        "On every craft step, pick this many top solutions and use them as " +
                        "the input for the next craft step. For best results, use Fork Count / 2 " +
                        "and add about 1 or 2 more if needed.\n" +
                        "(Only used in the Stepwise Genetic algorithm)",
                        config.FurcatedActionCount,
                        1,
                        500,
                        v => config = config with { FurcatedActionCount = v },
                        ref isDirty
                    );
            }
            else
            {
                DrawOption(
                "快速求解",
                    "Speeds up solve times. Backloads all Progress " +
                    "actions to the end of the rotation.",
                    config.BackloadProgress,
                    v => config = config with { BackloadProgress = v },
                    ref isDirty
                );
                DrawOption(
                "確保可靠度",
                    "Find a rotation that can reach the target quality no matter " +
                    "how unlucky the random conditions are.",
                    config.Adversarial,
                    v => config = config with { Adversarial = v },
                    ref isDirty
                );

                if (config.Adversarial)
                {
                    ImGui.SameLine();
                    using (var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange))
                    {
                        using var font = ImRaii.PushFont(UiBuilder.IconFont);
                        ImGui.TextUnformatted(FontAwesomeIcon.ExclamationCircle.ToIconString());
                    }
                    if (ImGui.IsItemHovered())
                        ImGuiUtils.TooltipWrapped("\"Ensure Reliability\" uses a lot more memory and can significantly increase solve times.");
                }
            }
        }

        using (var panel = ImRaii2.GroupPanel("技能池", -1, out var poolWidth))
        {
            poolWidth -= ImGui.GetStyle().ItemSpacing.X * 2;

            ImGui.TextUnformatted("選擇求解器可以使用的製作技能。");

            var pool = config.ActionPool;
            DrawActionPool(ref pool, poolWidth, out var isPoolDirty);
            if (isPoolDirty)
            {
                config = config with { ActionPool = pool };
                isDirty = true;
            }
        }

        if (config.Algorithm != SolverAlgorithm.Raphael)
        {
        using (var panel = ImRaii2.GroupPanel("進階", -1, out _))
            {
                DrawOption(
                "模擬步驟數量上限",
                    "The maximum number of crafting steps every iteration can consider. " +
                    "Decreasing this value can have unintended side effects. Only change " +
                    "this value if you absolutely know what you're doing.",
                    config.MaxRolloutStepCount,
                    1,
                    50,
                    v => config = config with { MaxRolloutStepCount = v },
                    ref isDirty
                );

                DrawOption(
                "嚴格限制技能",
                    "When finding the next possible actions to execute, use a heuristic " +
                    "to restrict which actions to attempt taking. This results in a much " +
                    "better macro at the cost of not finding an extremely creative one.",
                    config.StrictActions,
                    v => config = config with { StrictActions = v },
                    ref isDirty
                );
            }
        }

        if (config.Algorithm != SolverAlgorithm.Raphael)
        {
        using (var panel = ImRaii2.GroupPanel("分數權重（進階）", -1, out _))
            {
                DrawOption(
                "作業精度",
                    "Amount of weight to give to the craft's progress.",
                    config.ScoreProgress,
                    0,
                    100,
                    v => config = config with { ScoreProgress = v },
                    ref isDirty
                );

                DrawOption(
                "品質",
                    "Amount of weight to give to the craft's quality.",
                    config.ScoreQuality,
                    0,
                    100,
                    v => config = config with { ScoreQuality = v },
                    ref isDirty
                );

                DrawOption(
                "耐久",
                    "Amount of weight to give to the craft's remaining durability.",
                    config.ScoreDurability,
                    0,
                    100,
                    v => config = config with { ScoreDurability = v },
                    ref isDirty
                );

                DrawOption(
                    "CP",
                    "Amount of weight to give to the craft's remaining CP.",
                    config.ScoreCP,
                    0,
                    100,
                    v => config = config with { ScoreCP = v },
                    ref isDirty
                );

                DrawOption(
                "步驟",
                    "Amount of weight to give to the craft's number of steps. The lower " +
                    "the step count, the higher the score.",
                    config.ScoreSteps,
                    0,
                    100,
                    v => config = config with { ScoreSteps = v },
                    ref isDirty
                );
            }
        }

        if (isDirty)
            configRef = config;
    }

    private static void DrawActionPool(ref ActionType[] actionPool, float poolWidth, out bool isDirty)
    {
        isDirty = false;

        var recipeData = Service.Plugin.GetDefaultStats().Recipe;
        HashSet<ActionType> pool = [.. actionPool];

        var imageSize = ImGui.GetFrameHeight() * 2;
        var spacing = ImGui.GetStyle().ItemSpacing.Y;

        using var _color = ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero);
        using var _color3 = ImRaii.PushColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        using var _color2 = ImRaii.PushColor(ImGuiCol.ButtonActive, Vector4.Zero);
        using var _alpha = ImRaii.PushStyle(ImGuiStyleVar.DisabledAlpha, ImGui.GetStyle().DisabledAlpha * .5f);
        foreach (var category in Enum.GetValues<ActionCategory>())
        {
            if (category == ActionCategory.Combo)
                continue;

            var actions = category.GetActions();
            using var panel = ImRaii2.GroupPanel(category.GetDisplayName(), poolWidth, out var availSpace);
            var itemsPerRow = (int)MathF.Floor((availSpace + spacing) / (imageSize + spacing));
            var itemCount = actions.Count;
            var iterCount = (int)(Math.Ceiling((float)itemCount / itemsPerRow) * itemsPerRow);
            for (var i = 0; i < iterCount; i++)
            {
                if (i % itemsPerRow != 0)
                    ImGui.SameLine(0, spacing);
                if (i < itemCount)
                {
                    var actionBase = actions[i].Base();
                    var isEnabled = pool.Contains(actions[i]);
                    var isInefficient = SolverConfig.InefficientActions.Contains(actions[i]);
                    var isRisky = SolverConfig.RiskyActions.Contains(actions[i]);
                    var iconTint = Vector4.One;
                    if (!isEnabled)
                        iconTint = new(1, 1, 1, ImGui.GetStyle().DisabledAlpha);
                    else if (isInefficient)
                        iconTint = new(1, 1f, .5f, 1);
                    else if (isRisky)
                        iconTint = new(1, .5f, .5f, 1);
                    if (ImGui.ImageButton(actions[i].GetIcon(recipeData.ClassJob).Handle, new(imageSize), default, Vector2.One, 0, default, iconTint))
                    {
                        isDirty = true;
                        if (isEnabled)
                            pool.Remove(actions[i]);
                        else
                            pool.Add(actions[i]);
                    }
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        var s = new StringBuilder();
                        s.AppendLine(actions[i].GetName(recipeData.ClassJob));
                        if (isInefficient)
                            s.AppendLine(
                                "Not recommended. This action may be randomly used in a " +
                                "detrimental way to the rest of the craft. Always use " +
                                "your best judgement if enabling this action.");
                        if (isRisky)
                            s.AppendLine(
                                "Useless; the solver currently doesn't take any risks in " +
                                "its crafts. It only takes steps that have a 100% chance of " +
                                "succeeding. If you want have a moment where you want to take " +
                                "risks in your craft (like in expert recipes), don't rely " +
                                "on the solver during that time.");
                        ImGuiUtils.TooltipWrapped(s.ToString());
                    }
                }
                else
                    ImGui.Dummy(new(imageSize));
            }
        }

        if (isDirty)
        {
            bool InPool(BaseComboAction action)
            {
                if (action.ActionTypeA.Base() is BaseComboAction { } aCombo)
                {
                    if (!InPool(aCombo))
                        return false;
                }
                else
                {
                    if (!pool.Contains(action.ActionTypeA))
                        return false;
                }
                if (action.ActionTypeB.Base() is BaseComboAction { } bCombo)
                {
                    if (!InPool(bCombo))
                        return false;
                }
                else
                {
                    if (!pool.Contains(action.ActionTypeB))
                        return false;
                }
                return true;
            }

            foreach (var combo in ActionCategory.Combo.GetActions())
            {
                if (combo.Base() is BaseComboAction { } comboAction)
                {
                    if (!InPool(comboAction))
                        pool.Remove(combo);
                    else
                        pool.Add(combo);
                }
            }
            actionPool = [.. pool];
        }
    }

    private void DrawTabRecipeNote()
    {
        using var tab = TabItem("製作筆記###Crafting Log");
        if (!tab)
            return;

        ImGuiHelpers.ScaledDummy(5);

        var isDirty = false;

        DrawOption(
            "固定輔助視窗",
            "Pins the helper window to the right of your crafting log. Disabling this will " +
            "allow you to move it around.",
            Config.PinRecipeNoteToWindow,
            v => Config.PinRecipeNoteToWindow = v,
            ref isDirty
        );

        DrawOption(
            "總是收合輔助視窗",
            "Enabling this will cause the Helper Window to be collapsed whenever you start " +
            "a new craft, preventing the solver from running automatically.",
            Config.CollapseSynthHelper,
            v => Config.CollapseSynthHelper = v,
            ref isDirty
        );

        DrawOption(
            "自動建議巨集",
            "(Can cause frame drops!) When navigating to a new recipe or changing your gear " +
            "stats, automatically suggest a new macro (equivalent to clicking \"Generate\" " +
            "in the Macro Editor). This can cause harsh frame drops on some computers or " +
            "recipes when underleveled while navigating the crafting log. Turning this off " +
            "provides a button to allow you to manually suggest a macro only when you need it.",
            Config.SuggestMacroAutomatically,
            v => Config.SuggestMacroAutomatically = v,
            ref isDirty
        );

        DrawOption(
            "啟用社群巨集",
            "Use FFXIV Teamcraft's community rotations to search for and find the best possible " +
            "crowd-sourced macro for your craft. This sends a request to their servers to retrieve " +
            "a list of macros that apply to your craft's rlvl. Requests are only sent once per rlvl " +
            "and are always cached to reduce server load.",
            Config.ShowCommunityMacros,
            v => Config.ShowCommunityMacros = v,
            ref isDirty
        );

        if (Config.ShowCommunityMacros)
        {
            DrawOption(
            "自動搜尋社群巨集",
                "When navigating to a new recipe or changing your gear stats, automatically search " +
                "online for a new community macro.\n" +
                "This is turned off by default so you don't hammer their servers :)",
                Config.SearchCommunityMacroAutomatically,
                v => Config.SearchCommunityMacroAutomatically = v,
                ref isDirty
            );
        }

        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5);

        var solverConfig = Config.RecipeNoteSolverConfig;
        DrawSolverConfig(ref solverConfig, SolverConfig.RecipeNoteDefault, false, out var isSolverDirty);
        if (isSolverDirty)
        {
            Config.RecipeNoteSolverConfig = solverConfig;
            isDirty = true;
        }

        if (isDirty)
            Config.Save();
    }

    private void DrawTabMacroEditor()
    {
        using var tab = TabItem("巨集編輯器###Macro Editor");
        if (!tab)
            return;

        ImGuiHelpers.ScaledDummy(5);

        var isDirty = false;

        var solverConfig = Config.EditorSolverConfig;
        DrawSolverConfig(ref solverConfig, SolverConfig.EditorDefault, false, out var isSolverDirty);
        if (isSolverDirty)
        {
            Config.EditorSolverConfig = solverConfig;
            isDirty = true;
        }

        if (isDirty)
            Config.Save();
    }

    private void DrawTabSynthHelper()
    {
        using var tab = TabItem("製作輔助###Synthesis Helper");
        if (!tab)
            return;

        ImGuiHelpers.ScaledDummy(5);

        var isDirty = false;

        DrawOption(
            "固定輔助視窗",
            "Pins the synthesis helper to the right of your synthesis window. Disabling this will " +
            "allow you to move it around.",
            Config.PinSynthHelperToWindow,
            v => Config.PinSynthHelperToWindow = v,
            ref isDirty
        );

        DrawOption(
            "執行巨集時停用",
            "Disables itself when an in-game macro is running.",
            Config.DisableSynthHelperOnMacro,
            v => Config.DisableSynthHelperOnMacro = v,
            ref isDirty
        );

        DrawOption(
            "只模擬第一個步驟",
            "Only the first step is simulated by default. You can still " +
            "hover over the other steps to view their outcomes, but the " +
            "reliability trials (when hovering over the macro stats) are hidden.",
            Config.SynthHelperDisplayOnlyFirstStep,
            v => Config.SynthHelperDisplayOnlyFirstStep = v,
            ref isDirty
        );

        DrawOption(
            "顯示技能流動框",
            "Turns your hotbar into a whack-a-mole game! Draws ants for " +
            "the next action that should be executed. Also disables ants " +
            "for things like combo actions and condition procs.",
            Config.SynthHelperAbilityAnts,
            v => Config.SynthHelperAbilityAnts = v,
            ref isDirty
        );

        DrawOption(
            "求解步驟數量",
            "The minimum number of future steps to solve for during an in-game craft. " +
            "The solver may still give more than this amount if it's at no cost to you.",
            Config.SynthHelperStepCount,
            1,
            100,
            v => Config.SynthHelperStepCount = v,
            ref isDirty
        );

        DrawOption(
            "顯示步驟數量上限",
            "Enforces a maximum number of steps to display in the synth helper to " +
            "get rid of clutter.",
            Config.SynthHelperMaxDisplayCount,
            Config.SynthHelperStepCount,
            100,
            v => Config.SynthHelperMaxDisplayCount = v,
            ref isDirty
        );

        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5);

        var solverConfig = Config.SynthHelperSolverConfig;
        DrawSolverConfig(ref solverConfig, SolverConfig.SynthHelperDefault, true, out var isSolverDirty);
        if (isSolverDirty)
        {
            Config.SynthHelperSolverConfig = solverConfig;
            isDirty = true;
        }

        if (isDirty)
            Config.Save();
    }

    private void DrawTabAbout()
    {
        using var tab = TabItem("關於###About");
        if (!tab)
            return;

        ImGuiHelpers.ScaledDummy(5);

        var plugin = Service.Plugin;
        var icon = plugin.Icon;
        var iconDim = new Vector2(128) * ImGuiHelpers.GlobalScale;

        using (var table = ImRaii.Table("settingsAboutTable", 2))
        {
            if (table)
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, iconDim.X);

                ImGui.TableNextColumn();
                ImGui.Image(icon.Handle, iconDim);

                ImGui.TableNextColumn();
                ImGuiUtils.AlignMiddle(new(float.PositiveInfinity, HeaderFont.GetFontSize() + SubheaderFont.GetFontSize() + ImGui.GetFontSize() * 3 + ImGui.GetStyle().ItemSpacing.Y * 4), new(0, iconDim.Y));

                using (HeaderFont.Push())
                {
                    ImGuiUtils.AlignCentered(ImGui.CalcTextSize("Craftimizer").X);
                    ImGuiUtils.Hyperlink("Craftimizer", "https://github.com/WorkingRobot/Craftimizer", false);
                }

                using (SubheaderFont.Push())
                    ImGuiUtils.TextCentered($"v{plugin.Version} {plugin.BuildConfiguration}");

                ImGuiUtils.AlignCentered(ImGui.CalcTextSize($"By {plugin.Author} (WorkingRobot)").X);
                ImGui.TextUnformatted($"By {plugin.Author} (");
                ImGui.SameLine(0, 0);
                ImGuiUtils.Hyperlink("WorkingRobot", "https://github.com/WorkingRobot");
                ImGui.SameLine(0, 0);
                ImGui.TextUnformatted(")");

                using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.07f, 0.76f, 1.00f, 1f)))
                {
                    ImGuiUtils.AlignCentered(ImGui.CalcTextSize($"Support me on Ko-fi!").X);
                    ImGui.TextUnformatted($"Support me on ");
                    ImGui.SameLine(0, 0);
                    ImGuiUtils.Hyperlink("Ko-fi", Plugin.SupportLink);
                    ImGui.SameLine(0, 0);
                    ImGui.TextUnformatted("!");
                }
            }
        }

        ImGuiHelpers.ScaledDummy(5);

        ImGui.Separator();

        ImGuiHelpers.ScaledDummy(5);

        using (SubheaderFont.Push())
        ImGuiUtils.TextCentered("特別感謝");

        var startPosX = ImGui.GetCursorPosX();

        ImGuiUtils.TextWrappedTo("Thank you to ");
        ImGui.SameLine(0, 0);
        ImGuiUtils.Hyperlink("alostsock", "https://github.com/alostsock");
        ImGui.SameLine(0, 0);
        ImGuiUtils.TextWrappedTo(" for making ");
        ImGui.SameLine(0, 0);
        ImGuiUtils.Hyperlink("Craftingway", "https://craftingway.app");
        ImGui.SameLine(0, 0);
        ImGuiUtils.TextWrappedTo(" and the original solver algorithm.");

        ImGuiUtils.TextWrappedTo("Thank you to ");
        ImGui.SameLine(0, 0);
        ImGuiUtils.Hyperlink("KonaeAkira", "https://github.com/KonaeAkira");
        ImGui.SameLine(0, 0);
        ImGuiUtils.TextWrappedTo(" for making ");
        ImGui.SameLine(0, 0);
        ImGuiUtils.Hyperlink("raphael-rs", "https://raphael-xiv.com");
        ImGui.SameLine(0, 0);
        ImGuiUtils.TextWrappedTo(" and the Optimal algorithm.");

        ImGuiUtils.TextWrappedTo("Thank you to ");
        ImGui.SameLine(0, 0);
        ImGuiUtils.Hyperlink("FFXIV Teamcraft", "https://ffxivteamcraft.com");
        ImGui.SameLine(0, 0);
        ImGuiUtils.TextWrappedTo(" and its users for their community rotations.");

        ImGuiUtils.TextWrappedTo("Thank you to ");
        ImGui.SameLine(0, 0);
        ImGuiUtils.Hyperlink("this", "https://dke.maastrichtuniversity.nl/m.winands/documents/multithreadedMCTS2.pdf");
        ImGui.SameLine(0, 0);
        ImGuiUtils.TextWrappedTo(", ");
        ImGui.SameLine(0, 0);
        ImGuiUtils.Hyperlink("this", "https://liacs.leidenuniv.nl/~plaata1/papers/paper_ICAART18.pdf");
        ImGui.SameLine(0, 0);
        ImGuiUtils.TextWrappedTo(", and ");
        ImGui.SameLine(0, 0);
        ImGuiUtils.Hyperlink("this paper", "https://arxiv.org/abs/2308.04459");
        ImGui.SameLine(0, 0);
        ImGuiUtils.TextWrappedTo(" for inspiration and design references.");
    }

    public void Dispose()
    {
        Service.WindowSystem.RemoveWindow(this);
        SubheaderFont?.Dispose();
        HeaderFont?.Dispose();
    }
}
