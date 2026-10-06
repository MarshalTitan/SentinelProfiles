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
    private const string ProfilesPageId = "profiles";
    private const string AppearancePageId = "appearance";

    private static readonly Vector2 ClassicMinimumWindowSize = new(850f, 560f);

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
    private bool modernCollapsed;
    private bool expandOnNextDraw;
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
        if (expandOnNextDraw)
        {
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
            expandOnNextDraw = false;
            modernCollapsed = false;
        }

        if (modernThemeActive)
        {
            var scale = ImGuiHelpers.GlobalScale;
            Flags = SentinelModernWindowChrome.UseCustomHeader(classicWindowFlags);
            modernStyle.PushAppShell(scale);
            var shellMinimum = SentinelModernAppLayout.MinimumWindowSize(
                scale,
                hasSecondarySidebar: true);
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = Vector2.Max(ClassicMinimumWindowSize * scale, shellMinimum),
            };
        }
        else
        {
            Flags = classicWindowFlags;
            SizeConstraints = new WindowSizeConstraints { MinimumSize = ClassicMinimumWindowSize };
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
            EnableWindowDragging = true,
            ContextLabel = GetModernContextLabel(),
            Status = GetModernStatusPill(),
            RequestCollapse = requestModernCollapse,
            RequestClose = requestModernClose,
        };

        SentinelModernAppShell.Draw(
            options,
            modernShellState,
            ModernPrimaryNavigation,
            selectModernPrimaryPage,
            drawModernPage,
            modernPage == ModernPage.Profiles ? drawModernSecondaryNavigation : null);
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
        ImGui.SameLine();
        ImGui.TextDisabled("Manual plugin configuration switching");
        DrawThemeSelector();

        var lastApplied = profiles.LastAppliedProfile;
        var drift = driftDetector.Evaluate(lastApplied, discovery.Snapshot);
        ImGui.Text("Last Applied:");
        ImGui.SameLine();
        ImGui.TextColored(lastApplied is null ? Muted : Gold, lastApplied?.Name ?? "None");
        ImGui.SameLine();
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

        ImGui.SameLine();
        ImGui.BeginDisabled(lastApplied is null || coordinator.IsBusy);
        if (ImGui.SmallButton("Reapply Profile"))
            reapplyLastProfile(ApplyOrigin.UserInterface);
        ImGui.EndDisabled();

        DrawApplyFeedback(false);
    }

    private void DrawApplyFeedback(bool modern)
    {
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
            ImGui.ProgressBar(fraction, new Vector2(-1, 0), $"Applying {progress.ProfileName}: {action}");
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
            if (!modern)
                ImGui.SameLine();
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
                        ImGui.BulletText($"{problem.DisplayName}: {problem.Reason}");
                        if (configuration.ShowInternalNames
                            && !problem.DisplayName.Equals(problem.InternalName, StringComparison.OrdinalIgnoreCase))
                        {
                            ImGui.SameLine();
                            ImGui.TextDisabled($"[{problem.InternalName}]");
                        }
                    }

                    ImGui.TreePop();
                }
            }
        }

        if (coordinator.FatalError is { } fatalError)
            ImGui.TextColored(Red, fatalError);

        if (modern && (progress is not null || coordinator.LastResult is not null || coordinator.FatalError is not null))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }
    }

    private void DrawModernProfileNavigation()
    {
        var scale = ImGuiHelpers.GlobalScale;
        SentinelModernSecondaryNavigation.GroupLabel("Profiles");
        ImGui.Spacing();

        var available = ImGui.GetContentRegionAvail();
        var actionsReserve = 330f * scale;
        var profileListHeight = MathF.Max(104f * scale, available.Y - actionsReserve);
        var visible = ImGui.BeginChild(
            "##SentinelProfiles.Modern2.ProfileList",
            new Vector2(0f, profileListHeight));
        try
        {
            if (visible)
            {
                if (profiles.Profiles.Count == 0)
                {
                    ImGui.TextWrapped("No profiles yet. Create a blank profile to begin.");
                }
                else
                {
                    foreach (var profile in profiles.Profiles)
                    {
                        var selected = profiles.SelectedProfile?.Id == profile.Id;
                        var suffix = profiles.LastAppliedProfile?.Id == profile.Id ? "  • Last" : string.Empty;
                        if (SentinelModernSecondaryNavigation.Item(
                                $"SentinelProfiles.Profile.{profile.Id:N}",
                                profile.Name + suffix,
                                selected,
                                modernShellState.Motion,
                                scale))
                        {
                            profiles.Select(profile.Id);
                            selectedPlugins.Clear();
                        }
                    }
                }
            }
        }
        finally
        {
            ImGui.EndChild();
        }

        ImGui.TextDisabled("Last applied");
        ImGui.TextWrapped(profiles.LastAppliedProfile?.Name ?? "None");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        SentinelModernSecondaryNavigation.GroupLabel("Profile actions");
        ImGui.Spacing();
        DrawProfileActions(true);
    }

    private void DrawModernPage()
    {
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
                SentinelModernUi.SectionHeader("Get started");
                ImGui.TextWrapped(
                    "Use New Profile in the sidebar. Editing a profile never changes live plugins; changes happen only when you apply it.");
                DrawApplyFeedback(true);
            }

            return;
        }

        SentinelModernUi.PageHeading(
            profile.Name,
            $"{profile.PluginStates.Count} managed rule(s). Editing desired states does not change live plugins.");
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

        DrawApplyFeedback(true);
        DrawEditorPane(true);
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

        SentinelModernUi.SectionHeader("Theme");
        SentinelModernSettingsRow.Draw(
            "SentinelProfiles.Modern2.Theme",
            "Sentinel Modern 2",
            "Switch back to the original Classic layout. Profiles and every saved setting are preserved.",
            drawModernClassicThemeControl,
            controlWidth: 190f,
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
        expandOnNextDraw = true;
    }

    public void ToggleFromCommand()
    {
        if (!IsOpen || modernCollapsed)
        {
            OpenAndExpand();
            return;
        }

        IsOpen = false;
    }

    private void RequestModernCollapse()
    {
        modernCollapsed = true;
        ImGui.SetWindowCollapsed("Sentinel Profiles##SentinelProfiles-Main", true);
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
                new Vector2(-1f, 0f),
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

        if (profiles.Profiles.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("No profiles yet. Create a Blank Profile to choose only the plugins this profile should manage.");
        }
        else
        {
            foreach (var profile in profiles.Profiles)
            {
                var selected = profiles.SelectedProfile?.Id == profile.Id;
                if (ImGui.Selectable($"{profile.Name}##profile-{profile.Id}", selected))
                {
                    profiles.Select(profile.Id);
                    selectedPlugins.Clear();
                }

                if (profiles.LastAppliedProfile?.Id == profile.Id)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Gold, "Last");
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        DrawProfileActions();
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
                selectedProfile is null ? "Apply Profile" : $"Apply {selectedProfile.Name}",
                new Vector2(-1, 0));
        if (apply && selectedProfile is not null)
        {
            applyProfile(selectedProfile, ApplyOrigin.UserInterface);
        }
        ImGui.EndDisabled();

        ImGui.BeginDisabled(profiles.LastAppliedProfile is null || coordinator.IsBusy);
        if (ImGui.Button("Reapply Last Profile", new Vector2(-1, 0)))
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
            ImGui.TextColored(Gold, profile.Name.ToUpperInvariant());
            ImGui.SameLine();
            ImGui.TextDisabled($"{profile.PluginStates.Count} managed rule(s)");
            ImGui.TextDisabled("Editing desired states does not change live plugins. Apply the profile when ready.");
        }

        if (modern)
        {
            DrawModernEditorControls();
        }
        else
        {
            ImGui.SetNextItemWidth(Math.Max(180f, ImGui.GetContentRegionAvail().X * 0.48f));
            ImGui.InputTextWithHint("##plugin-search", "Search plugins...", ref search, 128);
            ImGui.SameLine();

            var filterIndex = (int)configuration.EditorFilter;
            ImGui.SetNextItemWidth(175f * ImGuiHelpers.GlobalScale);
            if (ImGui.Combo("##plugin-filter", ref filterIndex, FilterLabels, FilterLabels.Length))
            {
                configuration.EditorFilter = (EditorFilter)filterIndex;
                saveConfiguration();
            }

            ImGui.SameLine();
            var showInternalNames = configuration.ShowInternalNames;
            if (ImGui.Checkbox("Internal names", ref showInternalNames))
            {
                configuration.ShowInternalNames = showInternalNames;
                saveConfiguration();
            }
        }

        var rows = BuildRows(profile);
        selectedPlugins.RemoveWhere(internalName => rows.All(row => !row.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase)));
        DrawBulkControls(profile, rows, modern);
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
                "Show internal plugin names",
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
        IReadOnlyList<PluginEditorRow> rows,
        bool modern)
    {
        ImGui.TextDisabled($"{rows.Count} shown  |  {selectedPlugins.Count} selected");
        if (!modern)
            ImGui.SameLine();
        ImGui.BeginDisabled(selectedPlugins.Count == 0 || coordinator.IsBusy);
        ImGui.Text("Set Selected:");
        ImGui.SameLine();
        if (ImGui.SmallButton("Enable##bulk"))
            SetSelected(profile, rows, ProfilePluginState.Enable);
        ImGui.SameLine();
        if (ImGui.SmallButton("Leave Alone##bulk"))
            SetSelected(profile, rows, ProfilePluginState.LeaveAlone);
        ImGui.SameLine();
        if (ImGui.SmallButton("Disable##bulk"))
            SetSelected(profile, rows, ProfilePluginState.Disable);
        if (!modern || ImGui.GetContentRegionAvail().X >= 122f * ImGuiHelpers.GlobalScale)
            ImGui.SameLine();
        if (ImGui.SmallButton("Clear Selection"))
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
                    | ImGuiTableFlags.Resizable
                    | ImGuiTableFlags.ScrollY
                    | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("PluginProfileEditor", 4, flags, new Vector2(0, -1)))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 30f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthStretch, 0.44f);
        ImGui.TableSetupColumn("Current State", ImGuiTableColumnFlags.WidthFixed, 130f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Profile State", ImGuiTableColumnFlags.WidthStretch, 0.56f);
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
            ImGui.TextUnformatted(row.DisplayName);
            if (configuration.ShowInternalNames
                && !row.DisplayName.Equals(row.InternalName, StringComparison.OrdinalIgnoreCase))
            {
                ImGui.TextDisabled(row.InternalName);
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

            ImGui.TableSetColumnIndex(2);
            if (row.Plugin is null)
                DrawStatusValue("Missing", SentinelModernStatusTone.Warning, Orange, modern);
            else if (row.Plugin.IsLoaded)
                DrawStatusValue("Enabled", SentinelModernStatusTone.Success, Green, modern);
            else
                DrawStatusValue("Disabled", SentinelModernStatusTone.Neutral, Muted, modern);

            ImGui.TableSetColumnIndex(3);
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
        if (modern)
            SentinelModernUi.StatusChip(text, modernTone, ImGuiHelpers.GlobalScale);
        else
            ImGui.TextColored(classicColour, text);
    }

    private void DrawStateSelector(PluginProfile profile, PluginEditorRow row)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var buttonWidth = Math.Max(64f, (availableWidth - (spacing * 2)) / 3f);

        if (DrawStateButton("Enable", row.State == ProfilePluginState.Enable, EnableButton, buttonWidth))
            profiles.SetPluginState(profile, row.InternalName, row.DisplayName, ProfilePluginState.Enable);
        ImGui.SameLine();
        if (DrawStateButton("Leave Alone", row.State == ProfilePluginState.LeaveAlone, LeaveButton, buttonWidth))
            profiles.SetPluginState(profile, row.InternalName, row.DisplayName, ProfilePluginState.LeaveAlone);
        ImGui.SameLine();
        if (DrawStateButton("Disable", row.State == ProfilePluginState.Disable, DisableButton, buttonWidth))
            profiles.SetPluginState(profile, row.InternalName, row.DisplayName, ProfilePluginState.Disable);
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

        if (!ImGui.BeginPopupModal(
                "Create Profile##SentinelProfiles",
                ref newModalOpen,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.Text("Profile name");
        ImGui.SetNextItemWidth(360f * ImGuiHelpers.GlobalScale);
        ImGui.InputText("##new-profile-name", ref modalName, 64);
        if (modalError.Length > 0)
            ImGui.TextColored(Red, modalError);

        ImGui.Spacing();
        ImGui.TextWrapped("Blank Profile starts every installed plugin as literal Leave Alone. This is the normal, focused option.");
        if (ImGui.Button("Create Blank Profile", new Vector2(-1, 0)))
        {
            if (profiles.TryCreateBlank(modalName, out _, out modalError))
                CloseCurrentModal(ref newModalOpen);
        }

        ImGui.Spacing();
        ImGui.TextColored(Orange, "Capture Current Setup is aggressive.");
        ImGui.TextWrapped("It records every currently enabled plugin as Enable and every currently disabled plugin as Disable.");
        if (ImGui.Button("Capture Current Setup", new Vector2(-1, 0)))
        {
            if (profiles.TryCaptureCurrent(modalName, discovery.Snapshot, out _, out modalError))
                CloseCurrentModal(ref newModalOpen);
        }

        if (ImGui.Button("Cancel", new Vector2(-1, 0)))
            CloseCurrentModal(ref newModalOpen);

        ImGui.EndPopup();
    }

    private void DrawRenameModal()
    {
        if (!renameModalOpen)
            return;

        if (!ImGui.BeginPopupModal(
                "Rename Profile##SentinelProfiles",
                ref renameModalOpen,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.Text("New profile name");
        ImGui.SetNextItemWidth(360f * ImGuiHelpers.GlobalScale);
        ImGui.InputText("##rename-profile-name", ref modalName, 64);
        if (modalError.Length > 0)
            ImGui.TextColored(Red, modalError);

        if (ImGui.Button("Rename", new Vector2(175f * ImGuiHelpers.GlobalScale, 0)))
        {
            var profile = profiles.SelectedProfile;
            if (profile is not null && profiles.TryRename(profile, modalName, out modalError))
                CloseCurrentModal(ref renameModalOpen);
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(175f * ImGuiHelpers.GlobalScale, 0)))
            CloseCurrentModal(ref renameModalOpen);

        ImGui.EndPopup();
    }

    private void DrawDeleteModal()
    {
        if (!deleteModalOpen)
            return;

        if (!ImGui.BeginPopupModal(
                "Delete Profile##SentinelProfiles",
                ref deleteModalOpen,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        var profile = profiles.SelectedProfile;
        ImGui.TextWrapped(profile is null
            ? "The selected profile no longer exists."
            : $"Delete '{profile.Name}'? This removes the saved profile but does not change any live plugin states.");

        ImGui.BeginDisabled(profile is null);
        if (ImGui.Button("Delete", new Vector2(150f * ImGuiHelpers.GlobalScale, 0)) && profile is not null)
        {
            profiles.Delete(profile);
            selectedPlugins.Clear();
            CloseCurrentModal(ref deleteModalOpen);
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(150f * ImGuiHelpers.GlobalScale, 0)))
            CloseCurrentModal(ref deleteModalOpen);

        ImGui.EndPopup();
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
