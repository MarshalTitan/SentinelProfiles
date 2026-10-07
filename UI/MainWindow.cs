using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using SentinelCore.UI;
using SentinelProfiles.Core;
using SentinelProfiles.Models;
using SentinelProfiles.Services;

namespace SentinelProfiles.UI;

public sealed class MainWindow : Window
{
    private const float ProfilePaneWidth = 235f;
    private const float CompactStateButtonPadding = 4f;
    private const float CompactStateButtonSpacing = 4f;
    private const string ProfilesPageId = "profiles";
    private const string AppearancePageId = "appearance";

    private static readonly Vector2 ClassicMinimumWindowSize = new(680f, 560f);

    private static readonly SentinelModernAppLayoutOptions ModernLayout =
        SentinelModernAppLayoutOptions.Default with { SecondarySidebarWidth = ProfilePaneWidth };

    private static readonly SentinelModernNavItem[] ModernPrimaryNavigation =
    [
        new(ProfilesPageId, null, "Profiles")
        {
            DrawIcon = static context => DrawModernNavigationIcon(FontAwesomeIcon.Cog, context),
        },
        new(AppearancePageId, null, "Appearance")
        {
            DrawIcon = static context => DrawModernNavigationIcon(FontAwesomeIcon.Palette, context),
        },
    ];

    private static readonly string[] ConfigurationThemes = ["Classic", "Sentinel Modern"];

    private static readonly string[] FilterLabels =
    [
        "All Plugins",
        "Managed Only",
        "Enable entries",
        "Disable entries",
        "Leave Alone entries",
        "Missing Plugins",
    ];

    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.44f, 1f);
    private static readonly Vector4 Green = new(0.29f, 0.80f, 0.47f, 1f);
    private static readonly Vector4 Red = new(0.92f, 0.36f, 0.36f, 1f);
    private static readonly Vector4 Orange = new(0.96f, 0.65f, 0.28f, 1f);
    private static readonly Vector4 Muted = new(0.62f, 0.66f, 0.73f, 1f);
    private static readonly Vector4 EnableButton = new(0.12f, 0.47f, 0.25f, 1f);
    private static readonly Vector4 LeaveButton = new(0.34f, 0.36f, 0.42f, 1f);
    private static readonly Vector4 DisableButton = new(0.55f, 0.16f, 0.18f, 1f);

    private readonly Configuration configuration;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ProfileService profiles;
    private readonly PluginSafetyPolicy safetyPolicy;
    private readonly InstalledPluginDiscoveryService discovery;
    private readonly ApplicationCoordinator coordinator;
    private readonly DriftDetector driftDetector;
    private readonly Func<PluginProfile, ApplyOrigin, bool> applyProfile;
    private readonly Func<ApplyOrigin, bool> reapplyLastProfile;
    private readonly Action saveConfiguration;
    private readonly HashSet<string> selectedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly SentinelModernStyleScope modernStyle = new();
    private readonly SentinelModernAppShellState modernShellState = new();
    private readonly Action<string> selectModernPrimaryPage;
    private readonly Action drawModernPage;
    private readonly Action drawModernSecondaryNavigation;
    private readonly Action requestModernCollapse;
    private readonly Action requestModernClose;
    private readonly Action<SentinelModernIconDrawContext> drawModernPluginIcon;
    private readonly Action drawModernSearchControl;
    private readonly Action drawModernFilterControl;
    private readonly Action drawModernClassicThemeControl;
    private readonly ImGuiWindowFlags classicWindowFlags;

    private string search = string.Empty;
    private string modalName = string.Empty;
    private string modalError = string.Empty;
    private bool newModalOpen;
    private bool renameModalOpen;
    private bool deleteModalOpen;
    private bool modernThemeActive;
    private Vector2 modernFrameWindowSize;
    private Vector2? pendingModernSize;
    private ModernPage modernPage;

    public MainWindow(
        Configuration configuration,
        IDalamudPluginInterface pluginInterface,
        ProfileService profiles,
        PluginSafetyPolicy safetyPolicy,
        InstalledPluginDiscoveryService discovery,
        ApplicationCoordinator coordinator,
        DriftDetector driftDetector,
        Func<PluginProfile, ApplyOrigin, bool> applyProfile,
        Func<ApplyOrigin, bool> reapplyLastProfile,
        Action saveConfiguration)
        : base("Sentinel Profiles##SentinelProfiles-Main")
    {
        this.configuration = configuration;
        this.pluginInterface = pluginInterface;
        this.profiles = profiles;
        this.safetyPolicy = safetyPolicy;
        this.discovery = discovery;
        this.coordinator = coordinator;
        this.driftDetector = driftDetector;
        this.applyProfile = applyProfile;
        this.reapplyLastProfile = reapplyLastProfile;
        this.saveConfiguration = saveConfiguration;
        selectModernPrimaryPage = SelectModernPrimaryPage;
        drawModernPage = DrawModernPage;
        drawModernSecondaryNavigation = DrawModernProfileNavigation;
        requestModernCollapse = RequestModernCollapse;
        requestModernClose = RequestModernClose;
        drawModernPluginIcon = DrawModernPluginIcon;
        drawModernSearchControl = DrawModernSearchControl;
        drawModernFilterControl = DrawModernFilterControl;
        drawModernClassicThemeControl = DrawModernClassicThemeControl;
        classicWindowFlags = Flags;

        Size = new Vector2(1080, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ClassicMinimumWindowSize,
        };
    }

    public override void PreDraw()
    {
        modernStyle.Pop();
        modernThemeActive = SentinelThemeState<ModernPage>.NormalizeTheme(configuration.Theme)
                            == SentinelThemeKind.Modern;

        if (modernThemeActive)
        {
            var scale = ImGuiHelpers.GlobalScale;
            Flags = SentinelModernWindowChrome.UseCustomHeader(classicWindowFlags);
            modernStyle.PushAppShell(scale);
            var headerHeight = SentinelModernAppLayoutOptions.Default.HeaderHeight * scale;
            var shellMinimum = SentinelModernAppLayout.MinimumWindowSize(scale, hasSecondarySidebar: true, options: ModernLayout);
            var collapsedMinimum = SentinelModernAppLayout.MinimumWindowSize(scale, hasSecondarySidebar: false);
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = configuration.ModernWindowCollapsed
                    ? new Vector2(collapsedMinimum.X, headerHeight)
                    : Vector2.Max(ClassicMinimumWindowSize * scale, shellMinimum),
                MaximumSize = configuration.ModernWindowCollapsed
                    ? new Vector2(float.MaxValue, headerHeight)
                    : new Vector2(float.MaxValue, float.MaxValue),
            };

            // Modern collapse is represented by the custom header-height window, never by
            // ImGui's native collapsed title bar. This also recovers saved native-collapse
            // state left by the previous implementation.
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
        }
        else
        {
            Flags = classicWindowFlags;
            SizeConstraints = new WindowSizeConstraints { MinimumSize = ClassicMinimumWindowSize * ImGuiHelpers.GlobalScale };
        }

        if (pendingModernSize is { } requestedSize)
        {
            Size = requestedSize;
            SizeCondition = ImGuiCond.Always;
            pendingModernSize = null;
        }
        else
        {
            SizeCondition = ImGuiCond.FirstUseEver;
        }
    }

    public override void PostDraw()
    {
        if (modernThemeActive)
            modernStyle.Pop();
    }

    public override void Draw()
    {
        discovery.RefreshIfDue();
        if (modernThemeActive)
            DrawModernShell();
        else
            DrawClassicShell();

        DrawModals();
    }

    public void Dispose()
    {
        modernStyle.Dispose();
        modernShellState.Dispose();
    }

    private void DrawClassicShell()
    {
        DrawStatusHeader();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var available = ImGui.GetContentRegionAvail();
        available.Y = MathF.Max(120f * ImGuiHelpers.GlobalScale, available.Y);
        if (ImGui.BeginChild("ProfilesPane", new Vector2(ProfilePaneWidth * ImGuiHelpers.GlobalScale, available.Y), true))
            DrawProfilesPane();
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("EditorPane", new Vector2(0, available.Y), true))
            DrawEditorPane(false);
        ImGui.EndChild();
    }

    private void DrawModernShell()
    {
        modernFrameWindowSize = ImGui.GetWindowSize();
        var options = new SentinelModernAppShellOptions(
            "SentinelProfiles.Modern2",
            "Sentinel Profiles",
            GetModernPageId())
        {
            DrawPluginIcon = drawModernPluginIcon,
            Scale = ImGuiHelpers.GlobalScale,
            DeltaTime = ImGui.GetIO().DeltaTime,
            ReducedMotion = pluginInterface.UiBuilder.ShouldUseReducedMotion,
            AmbientIntensity = 0.9f,
            SurfaceStyle = SentinelModernAppSurfaceStyle.Unified,
            Layout = ModernLayout,
            EnableWindowDragging = true,
            ContextLabel = GetModernContextLabel(),
            Status = GetModernStatusPill(),
            RequestCollapse = requestModernCollapse,
            CollapseTooltip = configuration.ModernWindowCollapsed ? "Expand" : "Minimize",
            RequestClose = requestModernClose,
        };

        SentinelModernAppShell.Draw(
            options,
            modernShellState,
            ModernPrimaryNavigation,
            selectModernPrimaryPage,
            drawModernPage,
            !configuration.ModernWindowCollapsed && modernPage == ModernPage.Profiles
                ? drawModernSecondaryNavigation
                : null);
    }

    private void DrawModals()
    {

        if (newModalOpen)
            ImGui.OpenPopup("Create Profile##SentinelProfiles");
        if (renameModalOpen)
            ImGui.OpenPopup("Rename Profile##SentinelProfiles");
        if (deleteModalOpen)
            ImGui.OpenPopup("Delete Profile##SentinelProfiles");

        DrawNewProfileModal();
        DrawRenameModal();
        DrawDeleteModal();
    }

    private void DrawStatusHeader()
    {
        ImGui.TextColored(Gold, "SENTINEL PROFILES");
        ImGui.TextWrapped("Manual plugin configuration switching");
        DrawThemeSelector();

        var lastApplied = profiles.LastAppliedProfile;
        var drift = driftDetector.Evaluate(lastApplied, discovery.Snapshot);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(lastApplied is null ? Muted : Gold, $"Last applied: {lastApplied?.Name ?? "None"}");
        ImGui.PopTextWrapPos();
        ImGui.Text("Status:");
        ImGui.SameLine();
        switch (drift.State)
        {
            case DriftState.Matched:
                ImGui.TextColored(Green, "Matched");
                break;
            case DriftState.Drifted:
                ImGui.TextColored(Orange, "Modified / Drifted");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"{drift.MismatchedInternalNames.Count} state mismatch(es)");
                    ImGui.Text($"{drift.MissingInternalNames.Count} missing managed plugin(s)");
                    ImGui.EndTooltip();
                }
                break;
            default:
                ImGui.TextColored(Muted, "Not Applied");
                break;
        }

        ImGui.BeginDisabled(lastApplied is null || coordinator.IsBusy);
        if (ImGui.SmallButton("Reapply Profile"))
            reapplyLastProfile(ApplyOrigin.UserInterface);
        ImGui.EndDisabled();

        DrawApplyFeedback(false);
    }

    private void DrawApplyFeedback(bool modern)
    {
        ImGui.PushTextWrapPos(0f);
        var progress = coordinator.Progress;
        if (progress is not null)
        {
            var fraction = progress.TotalTransitions == 0
                ? 0f
                : (float)progress.CompletedTransitions / progress.TotalTransitions;
            var action = progress.TargetEnabled switch
            {
                true => $"Enabling {progress.CurrentPlugin}",
                false => $"Disabling {progress.CurrentPlugin}",
                _ => "Preparing and verifying",
            };
            ImGui.TextWrapped($"Applying {progress.ProfileName}: {action}");
            ImGui.ProgressBar(fraction, new Vector2(-1, 0));
            if (modern)
                ImGui.Spacing();
        }
        else if (coordinator.LastResult is { } result)
        {
            ImGui.TextColored(
                result.Succeeded ? Green : Orange,
                result.Succeeded
                    ? $"{result.ProfileName} applied successfully"
                    : $"{result.ProfileName} applied with {result.Problems.Count} problem(s)");
            ImGui.TextDisabled(
                $"{result.Enabled} enabled  |  {result.Disabled} disabled  |  "
                + $"{result.AlreadyCorrect} already correct  |  {result.LeftAlone} left alone");

            if (result.Problems.Count > 0)
            {
                ImGui.TextDisabled(
                    $"{result.Failed} failed  |  {result.Missing} missing  |  "
                    + $"{result.Unsupported} unsupported or protected");

                if (ImGui.TreeNode($"Problems ({result.Problems.Count})##last-apply-problems"))
                {
                    foreach (var problem in result.Problems)
                    {
                        ImGui.TextWrapped($"• {problem.DisplayName}: {problem.Reason}");
                        if (configuration.ShowInternalNames
                            && !problem.DisplayName.Equals(problem.InternalName, StringComparison.OrdinalIgnoreCase))
                        {
                            ImGui.TextDisabled($"[{problem.InternalName}]");
                        }
                    }

                    ImGui.TreePop();
                }
            }
        }

        if (coordinator.FatalError is { } fatalError)
            ImGui.TextColored(Red, fatalError);

        ImGui.PopTextWrapPos();
        if (modern && (progress is not null || coordinator.LastResult is not null || coordinator.FatalError is not null))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }
    }

    private void DrawModernProfileNavigation()
    {
        // Core's secondary surface deliberately has no scrolling. Own a normal scrolling
        // child for the entire profile list and actions so no fixed reserve can hide actions.
        var visible = ImGui.BeginChild("##SentinelProfiles.Modern2.ProfileNavigation", Vector2.Zero);
        try
        {
            if (!visible)
                return;

            SentinelModernSecondaryNavigation.GroupLabel("Profiles");
            ImGui.Spacing();
            DrawProfileList(true);
            ImGui.Spacing();
            ImGui.TextDisabled("Last applied");
            ImGui.TextWrapped(profiles.LastAppliedProfile?.Name ?? "None");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            SentinelModernSecondaryNavigation.GroupLabel("Profile actions");
            ImGui.Spacing();
            DrawProfileActions(true);
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private void DrawModernPage()
    {
        if (configuration.ModernWindowCollapsed)
            return;

        if (modernPage == ModernPage.Appearance)
        {
            DrawModernAppearancePage();
            return;
        }

        DrawModernProfilesPage();
    }

    private void DrawModernProfilesPage()
    {
        var profile = profiles.SelectedProfile;
        if (profile is null)
        {
            SentinelModernUi.PageHeading(
                "Profiles",
                "Create a profile to begin. Blank profiles leave every plugin alone until you choose otherwise.");
            ImGui.Spacing();
            using var emptyCard = SentinelModernGlassCard.Begin(
                "SentinelProfiles.Modern2.Empty",
                new SentinelModernGlassCardOptions
                {
                    Accent = SentinelModernPalette.Accent,
                    AccentStrength = 0.10f,
                    Elevated = true,
                },
                ImGuiHelpers.GlobalScale);
            if (emptyCard.IsVisible)
            {
                var emptyVisible = ImGui.BeginChild("##SentinelProfiles.EmptyScroll", Vector2.Zero);
                try
                {
                    if (emptyVisible)
                    {
                        SentinelModernUi.SectionHeader("Get started");
                        ImGui.TextWrapped(
                            "Use New Profile under Profile actions in the sidebar. Editing a profile never changes live plugins; changes happen only when you apply it.");
                        DrawApplyFeedback(true);
                    }
                }
                finally
                {
                    ImGui.EndChild();
                }
            }

            return;
        }

        ImGui.PushTextWrapPos(0f);
        SentinelModernUi.PageHeading(
            profile.Name,
            $"{profile.PluginStates.Count} managed rule(s). Editing desired states does not change live plugins.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        using var editorCard = SentinelModernGlassCard.Begin(
            "SentinelProfiles.Modern2.Editor",
            new SentinelModernGlassCardOptions
            {
                Accent = SentinelModernPalette.Accent,
                AccentStrength = 0.08f,
                Elevated = true,
            },
            ImGuiHelpers.GlobalScale);
        if (!editorCard.IsVisible)
            return;

        var visible = ImGui.BeginChild("##SentinelProfiles.EditorScroll", Vector2.Zero);
        try
        {
            if (visible)
            {
                DrawApplyFeedback(true);
                DrawEditorPane(true);
            }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private void DrawModernAppearancePage()
    {
        SentinelModernUi.PageHeading(
            "Appearance",
            "Choose the presentation for this window without changing any profile or plugin state.");
        ImGui.Spacing();
        using var card = SentinelModernGlassCard.Begin(
            "SentinelProfiles.Modern2.Appearance",
            new SentinelModernGlassCardOptions
            {
                Accent = SentinelModernPalette.Violet,
                AccentStrength = 0.12f,
                Elevated = true,
            },
            ImGuiHelpers.GlobalScale);
        if (!card.IsVisible)
            return;

        var visible = ImGui.BeginChild("##SentinelProfiles.AppearanceScroll", Vector2.Zero);
        try
        {
            if (!visible)
                return;

            SentinelModernUi.SectionHeader("Theme");
            SentinelModernSettingsRow.Draw(
                "SentinelProfiles.Modern2.Theme",
                "Sentinel Modern 2",
                "Switch back to the original Classic layout. Profiles and every saved setting are preserved.",
                drawModernClassicThemeControl,
                layoutOptions: SentinelModernSettingsRowLayoutOptions.Default with
                {
                    PreferredControlWidth = 190f,
                    MinimumControlWidth = 190f,
                },
                scale: ImGuiHelpers.GlobalScale);

            ImGui.Spacing();
            var reducedMotion = pluginInterface.UiBuilder.ShouldUseReducedMotion;
            SentinelModernStatusPill.Draw(
                new SentinelModernStatusPillOptions(
                    reducedMotion ? "REDUCED MOTION" : "STANDARD MOTION",
                    reducedMotion ? SentinelModernPillTone.Neutral : SentinelModernPillTone.Accent)
                {
                    Tooltip = "Motion follows Dalamud's accessibility preference.",
                },
                modernShellState.Motion,
                ImGuiHelpers.GlobalScale);
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private SentinelModernStatusPillOptions GetModernStatusPill()
    {
        if (coordinator.IsBusy)
        {
            return new SentinelModernStatusPillOptions("APPLYING", SentinelModernPillTone.Running)
            {
                Pulse = true,
            };
        }

        if (coordinator.FatalError is not null)
            return new SentinelModernStatusPillOptions("ERROR", SentinelModernPillTone.Error);

        var drift = driftDetector.Evaluate(profiles.LastAppliedProfile, discovery.Snapshot);
        return drift.State switch
        {
            DriftState.Matched => new SentinelModernStatusPillOptions("MATCHED", SentinelModernPillTone.Ready),
            DriftState.Drifted => new SentinelModernStatusPillOptions("DRIFTED", SentinelModernPillTone.Warning),
            _ => new SentinelModernStatusPillOptions("NOT APPLIED", SentinelModernPillTone.Neutral),
        };
    }

    private string GetModernPageId()
        => modernPage == ModernPage.Appearance ? AppearancePageId : ProfilesPageId;

    private string GetModernContextLabel()
        => modernPage == ModernPage.Appearance
            ? "Appearance"
            : profiles.SelectedProfile?.Name ?? "Profiles";

    private void SelectModernPrimaryPage(string id)
        => modernPage = id switch
        {
            ProfilesPageId => ModernPage.Profiles,
            AppearancePageId => ModernPage.Appearance,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown primary page."),
        };

    public void OpenAndExpand()
    {
        IsOpen = true;
        if (SentinelThemeState<ModernPage>.NormalizeTheme(configuration.Theme) == SentinelThemeKind.Modern
            && configuration.ModernWindowCollapsed)
        {
            ExpandModernWindow();
        }
    }

    public void ToggleFromCommand()
    {
        if (!IsOpen || configuration.ModernWindowCollapsed)
        {
            OpenAndExpand();
            return;
        }

        IsOpen = false;
    }

    private void RequestModernCollapse()
    {
        if (configuration.ModernWindowCollapsed)
        {
            ExpandModernWindow();
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        configuration.ModernExpandedWidth = modernFrameWindowSize.X / scale;
        configuration.ModernExpandedHeight = modernFrameWindowSize.Y / scale;
        pendingModernSize = new Vector2(
            modernFrameWindowSize.X,
            SentinelModernAppLayoutOptions.Default.HeaderHeight * scale);
        configuration.ModernWindowCollapsed = true;
        saveConfiguration();
    }

    private void ExpandModernWindow()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var minimumWidth = SentinelModernAppLayout.MinimumWindowSize(scale, hasSecondarySidebar: false).X;
        var currentWidth = modernFrameWindowSize.X > 0f
            ? modernFrameWindowSize.X
            : MathF.Max(minimumWidth, configuration.ModernExpandedWidth * scale);
        pendingModernSize = new Vector2(
            currentWidth,
            MathF.Max(ClassicMinimumWindowSize.Y, configuration.ModernExpandedHeight) * scale);
        configuration.ModernWindowCollapsed = false;
        saveConfiguration();
    }

    private void RequestModernClose() => IsOpen = false;

    private static void DrawModernPluginIcon(SentinelModernIconDrawContext context)
        => DrawFontAwesomeIcon(
            FontAwesomeIcon.ShieldAlt,
            context.DrawList,
            context.Minimum,
            context.Maximum,
            SentinelModernPalette.Text);

    private static void DrawModernNavigationIcon(
        FontAwesomeIcon icon,
        SentinelModernNavIconDrawContext context)
        => DrawFontAwesomeIcon(
            icon,
            context.DrawList,
            context.Minimum,
            context.Maximum,
            context.Colour);

    private static void DrawFontAwesomeIcon(
        FontAwesomeIcon icon,
        ImDrawListPtr drawList,
        Vector2 minimum,
        Vector2 maximum,
        Vector4 colour)
    {
        var glyph = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        try
        {
            var size = ImGui.CalcTextSize(glyph);
            drawList.AddText(
                minimum + (((maximum - minimum) - size) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(colour),
                glyph);
        }
        finally
        {
            ImGui.PopFont();
        }
    }

    private void DrawModernSearchControl()
        => ImGui.InputTextWithHint("##Value", "Search plugins...", ref search, 128);

    private void DrawModernFilterControl()
    {
        var filterIndex = (int)configuration.EditorFilter;
        if (!ImGui.Combo("##Value", ref filterIndex, FilterLabels, FilterLabels.Length))
            return;

        configuration.EditorFilter = (EditorFilter)filterIndex;
        saveConfiguration();
    }

    private void DrawModernClassicThemeControl()
    {
        if (!SentinelModernActionDock.PrimaryButton(
                "SentinelProfiles.SwitchClassic",
                "Switch to Classic",
                new Vector2(ImGui.CalcItemWidth(), ImGui.GetFrameHeight()),
                ImGuiHelpers.GlobalScale))
        {
            return;
        }

        configuration.Theme = (int)SentinelThemeKind.Classic;
        saveConfiguration();
    }

    private void DrawThemeSelector()
    {
        var selectedTheme = (int)SentinelThemeState<ModernPage>.NormalizeTheme(configuration.Theme);
        ImGui.SetNextItemWidth(modernThemeActive ? -1f : 180f * ImGuiHelpers.GlobalScale);
        if (!ImGui.Combo(
                "Theme##SentinelProfiles-Theme",
                ref selectedTheme,
                ConfigurationThemes,
                ConfigurationThemes.Length))
        {
            return;
        }

        configuration.Theme = selectedTheme;
        saveConfiguration();
    }

    private void DrawProfilesPane()
    {
        ImGui.TextColored(Gold, "PROFILES");
        ImGui.Separator();
        DrawProfileList(false);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Gold, "PROFILE ACTIONS");
        DrawProfileActions();
    }

    private void DrawProfileList(bool modern)
    {
        if (profiles.Profiles.Count == 0)
        {
            ImGui.TextWrapped("No profiles yet. Create a blank profile to begin.");
            return;
        }

        foreach (var profile in profiles.Profiles)
        {
            var selected = profiles.SelectedProfile?.Id == profile.Id;
            var suffix = profiles.LastAppliedProfile?.Id == profile.Id ? "  • Last" : string.Empty;
            var label = profile.Name + suffix;
            var scale = ImGuiHelpers.GlobalScale;
            var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
            if (modern && ImGui.CalcTextSize(label).X + 24f * scale <= width)
            {
                if (SentinelModernSecondaryNavigation.Item(
                        $"SentinelProfiles.Profile.{profile.Id:N}",
                        label, selected, modernShellState.Motion, scale))
                {
                    profiles.Select(profile.Id);
                    selectedPlugins.Clear();
                }
                continue;
            }

            // Long names retain native selectable activation and scroll-to-focus.
            var padding = ImGui.GetStyle().FramePadding;
            var textWidth = MathF.Max(1f, width - 2f * padding.X);
            var height = MathF.Max(ImGui.GetFrameHeight(),
                ImGui.CalcTextSize(label, false, textWidth).Y + 2f * padding.Y);
            var origin = ImGui.GetCursorScreenPos();
            if (ImGui.Selectable($"##profile-{profile.Id}", selected,
                    ImGuiSelectableFlags.None, new Vector2(width, height)))
            {
                profiles.Select(profile.Id);
                selectedPlugins.Clear();
            }
            var next = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(origin + padding);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
            ImGui.TextUnformatted(label);
            ImGui.PopTextWrapPos();
            ImGui.SetCursorScreenPos(next);
        }
    }

    private void DrawProfileActions(bool modern = false)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.BeginDisabled(coordinator.IsBusy);

        var create = modern
            ? SentinelModernActionDock.PrimaryButton(
                "SentinelProfiles.NewProfile",
                "New Profile",
                new Vector2(-1f, 0f),
                scale)
            : ImGui.Button("New Profile", new Vector2(-1, 0));
        if (create)
        {
            modalName = string.Empty;
            modalError = string.Empty;
            newModalOpen = true;
        }

        var selectedProfile = profiles.SelectedProfile;
        ImGui.BeginDisabled(selectedProfile is null);
        if (ImGui.Button("Duplicate", new Vector2(-1, 0)) && selectedProfile is not null)
        {
            profiles.Duplicate(selectedProfile);
            selectedPlugins.Clear();
        }

        if (ImGui.Button("Rename", new Vector2(-1, 0)) && selectedProfile is not null)
        {
            modalName = selectedProfile.Name;
            modalError = string.Empty;
            renameModalOpen = true;
        }

        var delete = modern
            ? SentinelModernActionDock.DangerButton(
                "SentinelProfiles.DeleteProfile",
                "Delete",
                new Vector2(-1f, 0f),
                scale)
            : ImGui.Button("Delete", new Vector2(-1, 0));
        if (delete && selectedProfile is not null)
        {
            deleteModalOpen = true;
        }
        ImGui.EndDisabled();

        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.BeginDisabled(selectedProfile is null || coordinator.IsBusy);
        var apply = modern
            ? SentinelModernActionDock.PrimaryButton(
                "SentinelProfiles.ApplyProfile",
                "Apply Selected",
                new Vector2(-1f, 0f),
                scale)
            : ImGui.Button(
                "Apply Selected",
                new Vector2(-1, 0));
        if (apply && selectedProfile is not null)
        {
            applyProfile(selectedProfile, ApplyOrigin.UserInterface);
        }
        ImGui.EndDisabled();

        ImGui.BeginDisabled(profiles.LastAppliedProfile is null || coordinator.IsBusy);
        if (ImGui.Button("Reapply Last", new Vector2(-1, 0)))
            reapplyLastProfile(ApplyOrigin.UserInterface);
        ImGui.EndDisabled();
    }

    private void DrawEditorPane(bool modern)
    {
        var profile = profiles.SelectedProfile;
        if (profile is null)
        {
            ImGui.TextColored(Gold, "PROFILE EDITOR");
            ImGui.Spacing();
            ImGui.TextWrapped("Create a profile to begin. Blank Profile is recommended: every plugin starts as literal Leave Alone and nothing changes until you click Apply.");
            return;
        }

        if (!modern)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Gold, profile.Name.ToUpperInvariant());
            ImGui.PopTextWrapPos();
            ImGui.TextDisabled($"{profile.PluginStates.Count} managed rule(s)");
            ImGui.TextWrapped("Editing desired states does not change live plugins. Apply the profile when ready.");
        }

        if (modern)
        {
            DrawModernEditorControls();
        }
        else
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##plugin-search", "Search plugins...", ref search, 128);

            var filterIndex = (int)configuration.EditorFilter;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.Combo("##plugin-filter", ref filterIndex, FilterLabels, FilterLabels.Length))
            {
                configuration.EditorFilter = (EditorFilter)filterIndex;
                saveConfiguration();
            }

            var showInternalNames = configuration.ShowInternalNames;
            if (ImGui.Checkbox("Internal names", ref showInternalNames))
            {
                configuration.ShowInternalNames = showInternalNames;
                saveConfiguration();
            }
        }

        var rows = BuildRows(profile);
        selectedPlugins.RemoveWhere(internalName => rows.All(row => !row.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase)));
        DrawBulkControls(profile, rows);
        ImGui.Spacing();
        DrawPluginTable(profile, rows, modern);
    }

    private void DrawModernEditorControls()
    {
        var scale = ImGuiHelpers.GlobalScale;
        SentinelModernSettingsRow.Draw(
            "SentinelProfiles.Modern2.Search",
            "Search",
            "Filter the installed and remembered plugin rows by display or internal name.",
            drawModernSearchControl,
            controlWidth: 230f,
            scale: scale);
        ImGui.Spacing();
        SentinelModernSettingsRow.Draw(
            "SentinelProfiles.Modern2.Filter",
            "Plugin filter",
            "Show all plugins or focus on one desired-state group.",
            drawModernFilterControl,
            controlWidth: 230f,
            scale: scale);
        ImGui.Spacing();

        var showInternalNames = configuration.ShowInternalNames;
        if (SentinelModernSwitch.Draw(
                "SentinelProfiles.Modern2.InternalNames",
                "Internal names",
                ref showInternalNames,
                modernShellState.Motion,
                scale))
        {
            configuration.ShowInternalNames = showInternalNames;
            saveConfiguration();
        }

        ImGui.Spacing();
    }

    private IReadOnlyList<PluginEditorRow> BuildRows(PluginProfile profile)
    {
        var rows = new Dictionary<string, PluginEditorRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in discovery.Snapshot)
        {
            var state = profile.GetState(plugin.InternalName);
            rows[plugin.InternalName] = new PluginEditorRow(
                plugin.InternalName,
                plugin.DisplayName,
                plugin,
                state,
                safetyPolicy.IsProtected(plugin.InternalName));
        }

        foreach (var (internalName, entry) in profile.PluginStates)
        {
            if (rows.ContainsKey(internalName))
                continue;

            rows[internalName] = new PluginEditorRow(
                internalName,
                string.IsNullOrWhiteSpace(entry.LastKnownDisplayName) ? internalName : entry.LastKnownDisplayName,
                null,
                entry.State,
                safetyPolicy.IsProtected(internalName));
        }

        return rows.Values
            .Where(MatchesSearch)
            .Where(MatchesFilter)
            .OrderBy(row => row.Plugin is null)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.InternalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool MatchesSearch(PluginEditorRow row)
        => string.IsNullOrWhiteSpace(search)
           || row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
           || row.InternalName.Contains(search, StringComparison.OrdinalIgnoreCase);

    private bool MatchesFilter(PluginEditorRow row)
        => configuration.EditorFilter switch
        {
            EditorFilter.AllPlugins => true,
            EditorFilter.ManagedOnly => row.State != ProfilePluginState.LeaveAlone,
            EditorFilter.Enable => row.State == ProfilePluginState.Enable,
            EditorFilter.Disable => row.State == ProfilePluginState.Disable,
            EditorFilter.LeaveAlone => row.State == ProfilePluginState.LeaveAlone,
            EditorFilter.MissingPlugins => row.Plugin is null,
            _ => true,
        };

    private void DrawBulkControls(
        PluginProfile profile,
        IReadOnlyList<PluginEditorRow> rows)
    {
        ImGui.TextDisabled($"{rows.Count} shown  |  {selectedPlugins.Count} selected");
        ImGui.BeginDisabled(selectedPlugins.Count == 0 || coordinator.IsBusy);
        ImGui.TextWrapped("Set Selected:");
        PushCompactStateButtonStyle();
        var layout = MeasureStateButtons(ImGui.GetContentRegionAvail().X);
        if (ImGui.Button("Enable##bulk", new Vector2(layout.EnableWidth, 0f)))
            SetSelected(profile, rows, ProfilePluginState.Enable);
        if (layout.Inline) ImGui.SameLine();
        if (ImGui.Button("Leave Alone##bulk", new Vector2(layout.LeaveAloneWidth, 0f)))
            SetSelected(profile, rows, ProfilePluginState.LeaveAlone);
        if (layout.Inline) ImGui.SameLine();
        if (ImGui.Button("Disable##bulk", new Vector2(layout.DisableWidth, 0f)))
            SetSelected(profile, rows, ProfilePluginState.Disable);
        ImGui.PopStyleVar(2);
        if (ImGui.Button("Clear Selection", new Vector2(-1f, 0f)))
            selectedPlugins.Clear();
        ImGui.EndDisabled();
    }

    private void SetSelected(
        PluginProfile profile,
        IReadOnlyList<PluginEditorRow> rows,
        ProfilePluginState state)
    {
        var byName = rows.ToDictionary(row => row.InternalName, StringComparer.OrdinalIgnoreCase);
        foreach (var internalName in selectedPlugins.ToArray())
        {
            if (byName.TryGetValue(internalName, out var row) && !row.IsProtected)
                profiles.SetPluginState(profile, row.InternalName, row.DisplayName, state);
        }
    }

    private void DrawPluginTable(
        PluginProfile profile,
        IReadOnlyList<PluginEditorRow> rows,
        bool modern)
    {
        var flags = ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.BordersInnerH
                    | ImGuiTableFlags.BordersInnerV
                    | ImGuiTableFlags.ScrollY
                    | ImGuiTableFlags.SizingStretchProp;

        var scale = ImGuiHelpers.GlobalScale;
        var style = ImGui.GetStyle();
        var stateWidth = ProfileEditorLayout.MinimumInlineWidth(
                             ImGui.CalcTextSize("Enable").X,
                             ImGui.CalcTextSize("Leave Alone").X,
                             ImGui.CalcTextSize("Disable").X,
                             CompactStateButtonPadding * scale,
                             CompactStateButtonSpacing * scale)
                         + 2f * style.CellPadding.X + 2f * scale;
        var currentWidth = MathF.Max(130f * scale, ImGui.CalcTextSize("Current State").X + 18f * scale);
        var selectionWidth = ImGui.GetFrameHeight();
        var compact = ImGui.GetContentRegionAvail().X
            < selectionWidth + currentWidth + stateWidth + 180f * scale + 8f * style.CellPadding.X;
        // Keep a usable table viewport even when wrapped controls consume the card's height.
        // The containing editor child scrolls normally to make the table reachable.
        var tableHeight = MathF.Max(160f * scale, ImGui.GetContentRegionAvail().Y - 1f);
        if (!ImGui.BeginTable("PluginProfileEditor", compact ? 2 : 4, flags, new Vector2(0, tableHeight)))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, selectionWidth);
        ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthStretch);
        if (!compact)
        {
            ImGui.TableSetupColumn("Current State", ImGuiTableColumnFlags.WidthFixed, currentWidth);
            ImGui.TableSetupColumn("Profile State", ImGuiTableColumnFlags.WidthFixed, stateWidth);
        }
        ImGui.TableHeadersRow();

        foreach (var row in rows)
        {
            ImGui.PushID(row.InternalName);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            if (row.IsProtected)
            {
                ImGui.TextDisabled("-");
            }
            else
            {
                var selected = selectedPlugins.Contains(row.InternalName);
                if (ImGui.Checkbox("##selected", ref selected))
                {
                    if (selected)
                        selectedPlugins.Add(row.InternalName);
                    else
                        selectedPlugins.Remove(row.InternalName);
                }
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.TextWrapped(row.DisplayName);
            if (configuration.ShowInternalNames
                && !row.DisplayName.Equals(row.InternalName, StringComparison.OrdinalIgnoreCase))
            {
                ImGui.PushTextWrapPos(0f);
                ImGui.TextDisabled(row.InternalName);
                ImGui.PopTextWrapPos();
            }

            if (row.IsProtected)
                DrawStatusValue("Protected", SentinelModernStatusTone.Violet, Gold, modern);
            else if (row.Plugin?.GetUnsupportedReason() is { } unsupported)
            {
                DrawStatusValue("Unsupported", SentinelModernStatusTone.Warning, Orange, modern);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(unsupported);
            }
            else if (row.Plugin is null)
                DrawStatusValue("Missing / Not Installed", SentinelModernStatusTone.Warning, Orange, modern);

            if (!compact)
                ImGui.TableSetColumnIndex(2);
            else
                ImGui.TextDisabled("Current state");
            if (row.Plugin is null)
                DrawStatusValue("Missing", SentinelModernStatusTone.Warning, Orange, modern);
            else if (row.Plugin.IsLoaded)
                DrawStatusValue("Enabled", SentinelModernStatusTone.Success, Green, modern);
            else
                DrawStatusValue("Disabled", SentinelModernStatusTone.Neutral, Muted, modern);

            if (!compact)
                ImGui.TableSetColumnIndex(3);
            else
                ImGui.TextDisabled("Profile state");
            if (row.IsProtected)
            {
                DrawStatusValue(
                    "Protected - always enabled",
                    SentinelModernStatusTone.Violet,
                    Gold,
                    modern);
            }
            else
            {
                ImGui.BeginDisabled(coordinator.IsBusy);
                DrawStateSelector(profile, row);
                ImGui.EndDisabled();
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private static void DrawStatusValue(
        string text,
        SentinelModernStatusTone modernTone,
        Vector4 classicColour,
        bool modern)
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (modern && ImGui.CalcTextSize(text).X + 18f * scale <= ImGui.GetContentRegionAvail().X)
            SentinelModernUi.StatusChip(text, modernTone, scale);
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text,
                modern ? SentinelModernPalette.ForStatus(modernTone) : classicColour);
            ImGui.TextWrapped(text);
            ImGui.PopStyleColor();
        }
    }

    private void DrawStateSelector(PluginProfile profile, PluginEditorRow row)
    {
        PushCompactStateButtonStyle();
        var layout = MeasureStateButtons(ImGui.GetContentRegionAvail().X);

        if (DrawStateButton("Enable", row.State == ProfilePluginState.Enable, EnableButton, layout.EnableWidth))
            profiles.SetPluginState(profile, row.InternalName, row.DisplayName, ProfilePluginState.Enable);
        if (layout.Inline) ImGui.SameLine();
        if (DrawStateButton("Leave Alone", row.State == ProfilePluginState.LeaveAlone, LeaveButton, layout.LeaveAloneWidth))
            profiles.SetPluginState(profile, row.InternalName, row.DisplayName, ProfilePluginState.LeaveAlone);
        if (layout.Inline) ImGui.SameLine();
        if (DrawStateButton("Disable", row.State == ProfilePluginState.Disable, DisableButton, layout.DisableWidth))
            profiles.SetPluginState(profile, row.InternalName, row.DisplayName, ProfilePluginState.Disable);
        ImGui.PopStyleVar(2);
    }

    private static (bool Inline, float EnableWidth, float LeaveAloneWidth, float DisableWidth)
        MeasureStateButtons(float availableWidth)
    {
        var style = ImGui.GetStyle();
        return ProfileEditorLayout.StateButtons(
            availableWidth,
            ImGui.CalcTextSize("Enable").X,
            ImGui.CalcTextSize("Leave Alone").X,
            ImGui.CalcTextSize("Disable").X,
            style.FramePadding.X,
            style.ItemSpacing.X);
    }

    private static void PushCompactStateButtonStyle()
    {
        var style = ImGui.GetStyle();
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,
            new Vector2(CompactStateButtonPadding * scale, style.FramePadding.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,
            new Vector2(CompactStateButtonSpacing * scale, style.ItemSpacing.Y));
    }

    private static bool DrawStateButton(string label, bool selected, Vector4 color, float width)
    {
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Brighten(color, 0.13f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Brighten(color, 0.22f));
        }

        var clicked = ImGui.Button(label, new Vector2(width, 0));
        if (selected)
            ImGui.PopStyleColor(3);
        return clicked;
    }

    private void DrawNewProfileModal()
    {
        if (!newModalOpen)
            return;

        if (!BeginProfileModal("Create Profile##SentinelProfiles", ref newModalOpen, 420f))
            return;

        ImGui.Text("Profile name");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##new-profile-name", ref modalName, 64);
        if (modalError.Length > 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Red, modalError);
            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Blank Profile starts every installed plugin as literal Leave Alone. This is the normal, focused option.");
        if (ImGui.Button("Create Blank Profile", new Vector2(-1, 0)))
        {
            if (profiles.TryCreateBlank(modalName, out _, out modalError))
                CloseCurrentModal(ref newModalOpen);
        }

        ImGui.Spacing();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(Orange, "Capture Current Setup is aggressive.");
        ImGui.PopTextWrapPos();
        ImGui.TextWrapped("It records every currently enabled plugin as Enable and every currently disabled plugin as Disable.");
        if (ImGui.Button("Capture Current Setup", new Vector2(-1, 0)))
        {
            if (profiles.TryCaptureCurrent(modalName, discovery.Snapshot, out _, out modalError))
                CloseCurrentModal(ref newModalOpen);
        }

        if (ImGui.Button("Cancel", new Vector2(-1, 0)))
            CloseCurrentModal(ref newModalOpen);

        EndProfileModal();
    }

    private void DrawRenameModal()
    {
        if (!renameModalOpen)
            return;

        if (!BeginProfileModal("Rename Profile##SentinelProfiles", ref renameModalOpen, 220f))
            return;

        ImGui.Text("New profile name");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##rename-profile-name", ref modalName, 64);
        if (modalError.Length > 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Red, modalError);
            ImGui.PopTextWrapPos();
        }

        if (ImGui.Button("Rename", new Vector2(-1f, 0f)))
        {
            var profile = profiles.SelectedProfile;
            if (profile is not null && profiles.TryRename(profile, modalName, out modalError))
                CloseCurrentModal(ref renameModalOpen);
        }

        if (ImGui.Button("Cancel", new Vector2(-1f, 0f)))
            CloseCurrentModal(ref renameModalOpen);

        EndProfileModal();
    }

    private void DrawDeleteModal()
    {
        if (!deleteModalOpen)
            return;

        if (!BeginProfileModal("Delete Profile##SentinelProfiles", ref deleteModalOpen, 240f))
            return;

        var profile = profiles.SelectedProfile;
        ImGui.TextWrapped(profile is null
            ? "The selected profile no longer exists."
            : $"Delete '{profile.Name}'? This removes the saved profile but does not change any live plugin states.");

        ImGui.BeginDisabled(profile is null);
        if (ImGui.Button("Delete", new Vector2(-1f, 0f)) && profile is not null)
        {
            profiles.Delete(profile);
            selectedPlugins.Clear();
            CloseCurrentModal(ref deleteModalOpen);
        }
        ImGui.EndDisabled();

        if (ImGui.Button("Cancel", new Vector2(-1f, 0f)))
            CloseCurrentModal(ref deleteModalOpen);

        EndProfileModal();
    }

    private static bool BeginProfileModal(string name, ref bool open, float preferredHeight)
    {
        PrepareProfileModal(preferredHeight);
        // The shared Modern app shell uses zero window padding for its full-bleed surface.
        // Popups need their own inset so labels and buttons do not touch the left edge.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            new Vector2(14f, 10f) * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginPopupModal(name, ref open, ImGuiWindowFlags.NoSavedSettings))
            return true;

        ImGui.PopStyleVar();
        return false;
    }

    private static void EndProfileModal()
    {
        ImGui.EndPopup();
        ImGui.PopStyleVar();
    }

    private static void PrepareProfileModal(float preferredHeight)
    {
        // A constrained initial size keeps every popup on screen at high UI scales.
        // Native ImGui resizing and scrolling remain available while it is open.
        var scale = ImGuiHelpers.GlobalScale;
        var display = ImGui.GetIO().DisplaySize;
        var maximum = new Vector2(
            MathF.Max(240f, display.X - 32f * scale),
            MathF.Max(180f, display.Y - 32f * scale));
        var minimum = Vector2.Min(new Vector2(340f, 190f) * scale, maximum);
        var initial = Vector2.Min(new Vector2(480f, preferredHeight) * scale, maximum);
        ImGui.SetNextWindowSizeConstraints(minimum, maximum);
        ImGui.SetNextWindowSize(initial, ImGuiCond.Appearing);
    }

    private static void CloseCurrentModal(ref bool open)
    {
        open = false;
        ImGui.CloseCurrentPopup();
    }

    private static Vector4 Brighten(Vector4 color, float amount)
        => new(
            Math.Min(1f, color.X + amount),
            Math.Min(1f, color.Y + amount),
            Math.Min(1f, color.Z + amount),
            color.W);

    private sealed record PluginEditorRow(
        string InternalName,
        string DisplayName,
        InstalledPluginInfo? Plugin,
        ProfilePluginState State,
        bool IsProtected);

    private enum ModernPage
    {
        Profiles,
        Appearance,
    }
}
