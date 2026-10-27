using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Role-specialized building inspection panel rendered for the currently selected building.
    ///
    /// Tailors its layout to the building's observed electrical role:
    /// - Generator inspection: output vs configured rating, operating state, network, controls
    /// - Battery inspection: prominent horizontal battery-charge indicator, stored/usable energy,
    ///   charging/discharging/EMP status, network association, and network endurance estimate
    /// - Consumer inspection: measured vs nominal draw, operating status, network association,
    ///   and automation/load-shedding settings &amp; controls
    /// </summary>
    public static class PowerBuildingInspector
    {
        private enum InspectorRole
        {
            Generator,
            Battery,
            Consumer
        }

        public static void Draw(
            Rect rect,
            PowerGridSnapshot gridSnapshot,
            Building selectedThing,
            BuildingPowerSnapshot building,
            PowerUiState state,
            PowerGridManagerSettings settings,
            PowerManagementController management,
            int currentTick)
        {
            if (rect.width <= 8f || rect.height <= 8f) return;

            if (building == null)
            {
                PowerUiDraw.Panel(rect);
                Rect innerMissing = rect.ContractedBy(10f);
                float btnW = Mathf.Min(90f, innerMissing.width * 0.3f);
                PowerUiDraw.Label(new Rect(innerMissing.x, innerMissing.y,
                        Mathf.Max(0f, innerMissing.width - btnW - 8f), innerMissing.height),
                    "PGM_Detail_SelectionMissing".Translate(),
                    GameFont.Small, PowerUiDraw.Warn, TextAnchor.MiddleLeft, true);
                Rect clearMissing = new Rect(innerMissing.xMax - btnW,
                    innerMissing.y + (innerMissing.height - 26f) * 0.5f, btnW, 26f);
                if (PowerUiDraw.Button(clearMissing, "PGM_Detail_Clear".Translate()))
                    state.SelectedBuilding = null;
                return;
            }

            InspectorRole role = ResolvePrimaryRole(building);
            Color roleAccent = RoleAccent(role, building, gridSnapshot, settings);
            PowerUiDraw.AccentCard(rect, roleAccent, false);

            Rect inner = new Rect(rect.x + 12f, rect.y + 8f,
                Mathf.Max(0f, rect.width - 22f), Mathf.Max(0f, rect.height - 16f));
            if (inner.width <= 12f || inner.height <= 12f) return;

            bool showMgmtBar = management != null && settings != null && settings.ManagementEnabled;
            float minBodyHeight = showMgmtBar ? 174f : 138f;
            if (inner.height < minBodyHeight && state != null)
            {
                Rect view = new Rect(0f, 0f, Mathf.Max(40f, inner.width - 16f), minBodyHeight);
                Widgets.BeginScrollView(inner, ref state.DetailScroll, view);
                try
                {
                    DrawInspectorBody(view, role, gridSnapshot, selectedThing, building, state, settings, management, currentTick, showMgmtBar);
                }
                finally
                {
                    Widgets.EndScrollView();
                }
                return;
            }

            DrawInspectorBody(inner, role, gridSnapshot, selectedThing, building, state, settings, management, currentTick, showMgmtBar);
        }

        private static void DrawInspectorBody(
            Rect inner,
            InspectorRole role,
            PowerGridSnapshot gridSnapshot,
            Building selectedThing,
            BuildingPowerSnapshot building,
            PowerUiState state,
            PowerGridManagerSettings settings,
            PowerManagementController management,
            int currentTick,
            bool showMgmtBar)
        {
            PowerNetworkSnapshot associatedNet = FindNetwork(gridSnapshot, building.NetworkId);

            Rect header = new Rect(inner.x, inner.y, inner.width, 28f);
            DrawHeader(header, role, selectedThing, building, associatedNet, settings, state);

            Widgets.DrawLineHorizontal(inner.x, header.yMax + 3f, inner.width, PowerUiDraw.BorderPanel);

            float bodyY = header.yMax + 8f;
            float mgmtH = showMgmtBar ? 30f : 0f;
            float bodyH = Mathf.Max(36f, inner.yMax - bodyY - (showMgmtBar ? mgmtH + 6f : 0f));
            Rect body = new Rect(inner.x, bodyY, inner.width, bodyH);

            switch (role)
            {
                case InspectorRole.Generator:
                    DrawGeneratorBody(body, selectedThing, building, associatedNet, settings);
                    break;
                case InspectorRole.Battery:
                    DrawBatteryBody(body, building, associatedNet, settings);
                    break;
                default:
                    DrawConsumerBody(body, selectedThing, building, associatedNet, settings, management);
                    break;
            }

            if (showMgmtBar)
            {
                Rect mgmtRect = new Rect(inner.x, inner.yMax - mgmtH, inner.width, mgmtH);
                DrawManagementControlsBar(mgmtRect, selectedThing, building, gridSnapshot, settings, management, currentTick);
            }
        }

        private static void DrawHeader(
            Rect header,
            InspectorRole role,
            Building selectedThing,
            BuildingPowerSnapshot building,
            PowerNetworkSnapshot associatedNet,
            PowerGridManagerSettings settings,
            PowerUiState state)
        {
            const float btnW = 74f;
            const float gap = 6f;
            float buttonsTotal = header.width >= 420f ? btnW * 3f + gap * 2f : 0f;

            if (buttonsTotal > 0f)
            {
                Rect clearRect = new Rect(header.xMax - btnW, header.y + 1f, btnW, 26f);
                Rect inspectRect = new Rect(clearRect.x - gap - btnW, header.y + 1f, btnW, 26f);
                Rect locateRect = new Rect(inspectRect.x - gap - btnW, header.y + 1f, btnW, 26f);

                bool canAct = selectedThing != null && !selectedThing.Destroyed && selectedThing.Spawned;
                if (PowerUiDraw.Button(locateRect, "PGM_Detail_Locate".Translate(), false, false, canAct))
                    CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(selectedThing));
                if (PowerUiDraw.Button(inspectRect, "PGM_Detail_Inspect".Translate(), false, false, selectedThing != null))
                    Find.WindowStack.Add(new Dialog_InfoCard(selectedThing));
                if (PowerUiDraw.Button(clearRect, "PGM_Detail_Clear".Translate()))
                    state.SelectedBuilding = null;
            }

            float availW = Mathf.Max(40f, header.width - buttonsTotal - (buttonsTotal > 0f ? 10f : 0f));

            string roleLabel = RoleBadgeLabel(role);
            float roleBadgeW = Mathf.Min(110f, Mathf.Max(68f, Text.CalcSize(roleLabel).x + 16f));
            Color roleCol = role == InspectorRole.Generator
                ? PowerUiDraw.AccentCyan
                : (role == InspectorRole.Battery ? PowerUiDraw.Good : PowerUiDraw.AccentPrimary);
            PowerUiDraw.StatusBadge(new Rect(header.x, header.y + 2f, roleBadgeW, 24f), roleLabel, roleCol);

            string statusText;
            Color statusCol;
            string statusSym;
            ResolveRoleStatus(role, building, associatedNet, settings, out statusText, out statusCol, out statusSym);
            float statusW = availW >= 300f
                ? Mathf.Min(154f, Mathf.Max(84f, Text.CalcSize(statusText).x + 26f))
                : 0f;
            if (statusW > 0f)
            {
                PowerUiDraw.StatusBadge(new Rect(header.x + availW - statusW, header.y + 2f, statusW, 24f),
                    statusText, statusCol, statusSym);
            }

            float titleX = header.x + roleBadgeW + 8f;
            float titleW = Mathf.Max(20f, availW - roleBadgeW - statusW - (statusW > 0f ? 16f : 8f));
            string bLabel = PowerGridUtilities.BuildingLabel(selectedThing);
            string defName = selectedThing != null && selectedThing.def != null ? selectedThing.def.defName : string.Empty;
            string fullTitle = string.IsNullOrEmpty(defName) ? bLabel : bLabel + "  (" + defName + ")";
            PowerUiDraw.Label(new Rect(titleX, header.y, titleW, header.height),
                fullTitle, GameFont.Small, PowerUiDraw.TextPrimary, TextAnchor.MiddleLeft);
        }

        private static void DrawGeneratorBody(
            Rect body,
            Building selectedThing,
            BuildingPowerSnapshot b,
            PowerNetworkSnapshot net,
            PowerGridManagerSettings settings)
        {
            float gap = 10f;
            bool multiCol = body.width >= 520f;
            float colW = multiCol ? (body.width - gap * 2f) / 3f : body.width;

            // Column 1: Current Output Card.
            Rect c1 = new Rect(body.x, body.y, colW, body.height);
            PowerUiDraw.ElevatedPanel(c1);
            Rect p1 = c1.ContractedBy(8f);
            PowerUiDraw.Label(new Rect(p1.x, p1.y, p1.width, 16f),
                "PGM_Inspector_GenOutputTitle".Translate(), GameFont.Small, PowerUiDraw.TextSecondary);
            Color outCol = b.GenerationWatts > 0.01f ? PowerUiDraw.AccentCyan : PowerUiDraw.Warn;
            PowerUiDraw.Label(new Rect(p1.x, p1.y + 17f, p1.width, 26f),
                PowerFormatting.FormatWatts(b.GenerationWatts), GameFont.Medium, outCol);

            if (p1.height >= 62f)
            {
                float cfg = b.ConfiguredGenerationWatts ?? 0f;
                float ratio = cfg > 0.01f ? Mathf.Clamp01(b.GenerationWatts / cfg) : (b.GenerationWatts > 0.01f ? 1f : 0f);
                PowerUiDraw.Bar(new Rect(p1.x, p1.y + 45f, p1.width, 8f), ratio, outCol);
                if (p1.height >= 76f)
                {
                    PowerUiDraw.KeyValueRow(new Rect(p1.x, p1.y + 56f, p1.width, 18f),
                        "PGM_Tile_ConfiguredGeneration".Translate(),
                        PowerFormatting.FormatWattsUnknown(b.ConfiguredGenerationWatts),
                        PowerUiDraw.TextPrimary,
                        "PGM_Tip_ConfiguredGeneration".Translate());
                }
            }

            if (!multiCol) return;

            // Column 2: Operating Information.
            Rect c2 = new Rect(c1.xMax + gap, body.y, colW, body.height);
            PowerUiDraw.ElevatedPanel(c2);
            Rect p2 = c2.ContractedBy(8f);
            float y = p2.y;
            bool traderOn = !b.HasOffComponents;
            PowerUiDraw.KeyValueRow(new Rect(p2.x, y, p2.width, 18f),
                "PGM_Detail_TraderState".Translate(), TraderStateText(b),
                traderOn ? PowerUiDraw.Good : PowerUiDraw.Warn);
            y += 19f;
            PowerUiDraw.KeyValueRow(new Rect(p2.x, y, p2.width, 18f),
                "PGM_Inspector_SwitchState".Translate(),
                b.IsFlickable
                    ? (b.SwitchIsOn == false ? (string)"PGM_TraderState_Off".Translate() : (string)"PGM_TraderState_On".Translate())
                    : (string)"PGM_Value_NotApplicable".Translate(),
                b.SwitchIsOn == false ? PowerUiDraw.Warn : PowerUiDraw.TextPrimary);
            y += 19f;
            if (p2.yMax - y >= 18f)
            {
                PowerUiDraw.KeyValueRow(new Rect(p2.x, y, p2.width, 18f),
                    "PGM_Detail_Room".Translate(), PowerGridUtilities.RoomLabel(b.Room));
                y += 19f;
            }
            if (p2.yMax - y >= 18f && selectedThing != null)
            {
                PowerUiDraw.KeyValueRow(new Rect(p2.x, y, p2.width, 18f),
                    "PGM_Detail_Position".Translate(),
                    PowerFormatting.FormatPosition(selectedThing.Position.x, selectedThing.Position.z));
            }

            // Column 3: Network Association & Compatibility.
            Rect c3 = new Rect(c2.xMax + gap, body.y, colW, body.height);
            PowerUiDraw.ElevatedPanel(c3);
            Rect p3 = c3.ContractedBy(8f);
            DrawNetworkAndSupportRows(p3, b, net, settings, false);
        }

        private static void DrawBatteryBody(
            Rect body,
            BuildingPowerSnapshot b,
            PowerNetworkSnapshot net,
            PowerGridManagerSettings settings)
        {
            float gap = 10f;
            bool multiCol = body.width >= 520f;
            float leftW = multiCol ? Mathf.Floor((body.width - gap) * 0.54f) : body.width;
            float rightW = multiCol ? body.width - leftW - gap : 0f;

            // Left Card: Prominent Charge Percentage + Horizontal Battery Charge Bar + Stored Energy.
            Rect c1 = new Rect(body.x, body.y, leftW, body.height);
            PowerUiDraw.ElevatedPanel(c1);
            Rect p1 = c1.ContractedBy(8f);

            float? frac = b.CapacityWattDays > 0f ? (float?)(b.StoredWattDays / b.CapacityWattDays) : null;
            Color chargeCol = PowerUiDraw.ChargeColor(frac, settings.LowBatteryWarningFraction);

            PowerUiDraw.Label(new Rect(p1.x, p1.y, p1.width * 0.46f, 24f),
                PowerFormatting.FormatCharge(frac), GameFont.Medium, chargeCol, TextAnchor.MiddleLeft);

            string storedSummary = PowerFormatting.FormatEnergy(b.StoredWattDays)
                + " / " + PowerFormatting.FormatEnergy(b.CapacityWattDays);
            PowerUiDraw.Label(new Rect(p1.x + p1.width * 0.42f, p1.y, p1.width * 0.58f, 24f),
                storedSummary, GameFont.Small, PowerUiDraw.TextPrimary, TextAnchor.MiddleRight);

            if (p1.height >= 44f)
            {
                Rect barRect = new Rect(p1.x, p1.y + 27f, p1.width, 14f);
                PowerUiDraw.BatteryBar(barRect, frac ?? 0f, chargeCol, settings.LowBatteryWarningFraction);
            }

            if (p1.height >= 66f)
            {
                float usable = SumBuildingAvailableWattDays(b);
                bool empDiff = b.StoredWattDays - usable > 0.02f;
                string usableLabel = empDiff
                    ? (string)"PGM_Overview_UsableVsStored".Translate(
                        PowerFormatting.FormatEnergy(usable),
                        PowerFormatting.FormatEnergy(b.StoredWattDays))
                    : PowerFormatting.FormatEnergy(usable);
                PowerUiDraw.KeyValueRow(new Rect(p1.x, p1.y + 46f, p1.width, 18f),
                    "PGM_Overview_UsableReserve".Translate(),
                    usableLabel,
                    empDiff ? PowerUiDraw.NoticeColor : PowerUiDraw.TextPrimary);

                if (p1.height >= 84f)
                {
                    float? eff = ReadBatteryEfficiency(b);
                    PowerUiDraw.KeyValueRow(new Rect(p1.x, p1.y + 64f, p1.width, 18f),
                        "PGM_Inspector_Efficiency".Translate(),
                        eff.HasValue ? PowerFormatting.FormatCharge(eff) : (string)"PGM_Value_Unknown".Translate(),
                        PowerUiDraw.TextSecondary);
                }
            }

            if (!multiCol) return;

            // Right Card: Network Association & Estimated Endurance.
            Rect c2 = new Rect(c1.xMax + gap, body.y, rightW, body.height);
            PowerUiDraw.ElevatedPanel(c2);
            Rect p2 = c2.ContractedBy(8f);
            DrawNetworkAndSupportRows(p2, b, net, settings, true);
        }

        private static void DrawConsumerBody(
            Rect body,
            Building selectedThing,
            BuildingPowerSnapshot b,
            PowerNetworkSnapshot net,
            PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            float gap = 10f;
            bool multiCol = body.width >= 520f;
            float colW = multiCol ? (body.width - gap * 2f) / 3f : body.width;

            // Column 1: Current Consumption & Daily Estimate.
            Rect c1 = new Rect(body.x, body.y, colW, body.height);
            PowerUiDraw.ElevatedPanel(c1);
            Rect p1 = c1.ContractedBy(8f);
            PowerUiDraw.Label(new Rect(p1.x, p1.y, p1.width, 16f),
                "PGM_Detail_CurrentDraw".Translate(), GameFont.Small, PowerUiDraw.TextSecondary);

            bool high = PowerGridUtilities.IsHighConsumption(b, settings.HighConsumptionWatts);
            Color drawCol = high ? PowerUiDraw.Warn : PowerUiDraw.TextPrimary;
            PowerUiDraw.Label(new Rect(p1.x, p1.y + 17f, p1.width, 26f),
                PowerFormatting.FormatWatts(b.ConsumptionWatts), GameFont.Medium, drawCol);

            if (p1.height >= 58f)
            {
                PowerUiDraw.KeyValueRow(new Rect(p1.x, p1.y + 44f, p1.width, 18f),
                    "PGM_Detail_NominalDraw".Translate(),
                    PowerFormatting.FormatWattsUnknown(b.NominalConsumptionWatts),
                    PowerUiDraw.TextPrimary,
                    "PGM_Tip_NominalDraw".Translate());
            }
            if (p1.height >= 76f)
            {
                string dailyText = b.IsConsumer
                    ? PowerFormatting.FormatEnergy(PowerFormatting.EstimateDailyWattDays(b.ConsumptionWatts))
                    : (string)"PGM_Value_NotApplicable".Translate();
                PowerUiDraw.KeyValueRow(new Rect(p1.x, p1.y + 62f, p1.width, 18f),
                    "PGM_Detail_EstDailyEnergy".Translate(),
                    dailyText,
                    PowerUiDraw.TextSecondary,
                    "PGM_Tip_EstDailyEnergy".Translate());
            }

            if (!multiCol) return;

            // Column 2: Operating Status & Network Association.
            Rect c2 = new Rect(c1.xMax + gap, body.y, colW, body.height);
            PowerUiDraw.ElevatedPanel(c2);
            Rect p2 = c2.ContractedBy(8f);
            float y = p2.y;
            PowerUiDraw.KeyValueRow(new Rect(p2.x, y, p2.width, 18f),
                "PGM_Detail_Category".Translate(),
                PowerGridUtilities.CategoryLabelKey(b.Category).Translate());
            y += 19f;
            PowerUiDraw.KeyValueRow(new Rect(p2.x, y, p2.width, 18f),
                "PGM_Detail_Network".Translate(),
                NetworkSummaryText(b, net));
            y += 19f;
            if (p2.yMax - y >= 18f)
            {
                PowerUiDraw.KeyValueRow(new Rect(p2.x, y, p2.width, 18f),
                    "PGM_Detail_TraderState".Translate(),
                    TraderStateText(b),
                    !b.HasOffComponents ? PowerUiDraw.Good : PowerUiDraw.Warn);
                y += 19f;
            }
            if (p2.yMax - y >= 18f)
            {
                string posOrRoom = b.HasRoom
                    ? PowerGridUtilities.RoomLabel(b.Room)
                    : (selectedThing != null
                        ? PowerFormatting.FormatPosition(selectedThing.Position.x, selectedThing.Position.z)
                        : PowerGridUtilities.RoomLabel(b.Room));
                PowerUiDraw.KeyValueRow(new Rect(p2.x, y, p2.width, 18f),
                    b.HasRoom ? (string)"PGM_Detail_Room".Translate() : (string)"PGM_Detail_Position".Translate(),
                    posOrRoom);
            }

            // Column 3: Automation & Control Settings.
            Rect c3 = new Rect(c2.xMax + gap, body.y, colW, body.height);
            PowerUiDraw.ElevatedPanel(c3);
            Rect p3 = c3.ContractedBy(8f);
            DrawAutomationStatusRows(p3, selectedThing, b, settings, management);
        }

        private static void DrawNetworkAndSupportRows(
            Rect p,
            BuildingPowerSnapshot b,
            PowerNetworkSnapshot net,
            PowerGridManagerSettings settings,
            bool includeEndurance)
        {
            float y = p.y;
            PowerUiDraw.KeyValueRow(new Rect(p.x, y, p.width, 18f),
                "PGM_Detail_Network".Translate(),
                NetworkSummaryText(b, net),
                net != null ? PowerUiDraw.StatusColor(PowerGridUtilities.EvaluateStatus(net, settings.HighLoadThreshold)) : PowerUiDraw.Warn);
            y += 19f;

            if (net != null && p.yMax - y >= 18f)
            {
                PowerUiDraw.KeyValueRow(new Rect(p.x, y, p.width, 18f),
                    "PGM_Detail_Net".Translate(),
                    PowerFormatting.FormatWattsSigned(net.NetWatts),
                    PowerUiDraw.SignedColor(net.NetWatts));
                y += 19f;
            }

            if (includeEndurance && p.yMax - y >= 18f)
            {
                string endurance = net != null
                    ? FormatNetworkEndurance(BatteryForecast.ForNetwork(net, settings.ForecastIncludeSelfDischarge))
                    : (string)"PGM_Value_NotApplicable".Translate();
                PowerUiDraw.KeyValueRow(new Rect(p.x, y, p.width, 18f),
                    "PGM_Detail_Endurance".Translate(),
                    endurance,
                    PowerUiDraw.TextPrimary,
                    "PGM_Tip_NetworkEndurance".Translate());
                y += 19f;
            }

            if (p.yMax - y >= 18f)
            {
                PowerSupportLevel support = CompatibilityRegistry.Evaluate(b);
                PowerUiDraw.KeyValueRow(new Rect(p.x, y, p.width, 18f),
                    "PGM_Detail_Support".Translate(),
                    CompatibilityRegistry.LabelKey(support).Translate(),
                    support == PowerSupportLevel.Supported ? PowerUiDraw.Good : PowerUiDraw.Warn,
                    CompatibilityRegistry.TooltipKey(support).Translate());
                y += 19f;
            }

            if (p.yMax - y >= 18f)
            {
                PowerUiDraw.KeyValueRow(new Rect(p.x, y, p.width, 18f),
                    "PGM_Detail_History".Translate(),
                    "PGM_Value_NotRecorded".Translate(),
                    PowerUiDraw.TextSecondary,
                    "PGM_Tip_HistoryPerBuilding".Translate());
            }
        }

        private static void DrawAutomationStatusRows(
            Rect p,
            Building selectedThing,
            BuildingPowerSnapshot b,
            PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            PowerManagementRegistry registry = management != null ? management.Registry : null;
            PowerManagementRecord record = registry != null && selectedThing != null ? registry.Find(selectedThing) : null;
            PowerControlSupport support = CompatibilityRegistry.EvaluateControl(b, registry);
            bool usable = support != PowerControlSupport.Unsupported && support != PowerControlSupport.Pending;

            bool sheddable = record != null && record.Sheddable;
            int priority = record != null ? record.ShedPriority : PowerManagementRegistry.DefaultPriority;
            string group = record != null ? record.Group : string.Empty;

            float y = p.y;
            string policyText = sheddable
                ? "PGM_Heatmap_Policy_Sheddable".Translate() + " (P" + priority + ")"
                : (string)"PGM_Heatmap_Policy_Protected".Translate();
            PowerUiDraw.KeyValueRow(new Rect(p.x, y, p.width, 18f),
                "PGM_Inspector_AutoPolicy".Translate(),
                policyText,
                sheddable ? PowerUiDraw.Warn : PowerUiDraw.Good);
            y += 19f;

            if (p.yMax - y >= 18f)
            {
                PowerUiDraw.KeyValueRow(new Rect(p.x, y, p.width, 18f),
                    "PGM_Inspector_ControlPath".Translate(),
                    CompatibilityRegistry.ControlLabelKey(support).Translate(),
                    usable ? PowerUiDraw.Good : PowerUiDraw.TextSecondary,
                    CompatibilityRegistry.ControlTooltipKey(support).Translate());
                y += 19f;
            }

            if (p.yMax - y >= 18f)
            {
                PowerUiDraw.KeyValueRow(new Rect(p.x, y, p.width, 18f),
                    "PGM_Inspector_GroupLabel".Translate(),
                    string.IsNullOrEmpty(group) ? (string)"PGM_Group_None".Translate() : group,
                    PowerUiDraw.TextPrimary);
                y += 19f;
            }

            if (p.yMax - y >= 18f)
            {
                string modeState = settings.ManagementEnabled
                    ? (settings.LoadSheddingEnabled
                        ? (string)"PGM_Mgmt_Status_Enabled".Translate()
                        : (string)"PGM_Inspector_ManualOnly".Translate())
                    : (string)"PGM_Mgmt_Status_Off".Translate();
                PowerUiDraw.KeyValueRow(new Rect(p.x, y, p.width, 18f),
                    "PGM_Tab_Management".Translate(),
                    modeState,
                    settings.ManagementEnabled ? PowerUiDraw.AccentCyan : PowerUiDraw.TextSecondary);
            }
        }

        private static void DrawManagementControlsBar(
            Rect rect,
            Building selectedThing,
            BuildingPowerSnapshot b,
            PowerGridSnapshot mapSnapshot,
            PowerGridManagerSettings settings,
            PowerManagementController management,
            int currentTick)
        {
            PowerUiDraw.RecessedBox(rect);
            if (management == null || selectedThing == null)
            {
                PowerUiDraw.Label(rect.ContractedBy(6f),
                    "PGM_Mgmt_Cap_UnsupportedControl".Translate(),
                    GameFont.Small, PowerUiDraw.TextSecondary);
                return;
            }

            PowerManagementRegistry registry = management.Registry;
            PowerManagementRecord record = registry != null ? registry.Find(selectedThing) : null;
            PowerControlSupport support = CompatibilityRegistry.EvaluateControl(b, registry);
            bool usable = support != PowerControlSupport.Unsupported && support != PowerControlSupport.Pending;
            bool sheddable = record != null && record.Sheddable;
            bool excludedDiag = record != null && record.ExcludeFromDiagnostics;
            int priority = record != null ? record.ShedPriority : PowerManagementRegistry.DefaultPriority;

            float x = rect.x + 6f;
            float btnH = Mathf.Max(18f, rect.height - 6f);
            float btnY = rect.y + (rect.height - btnH) * 0.5f;
            const float gap = 6f;

            Rect onBtn = new Rect(x, btnY, 54f, btnH);
            if (PowerUiDraw.Button(onBtn, "PGM_Mgmt_Action_On".Translate(), b.SwitchIsOn == true && usable, false, usable))
            {
                string detail;
                management.ManualSwitch(mapSnapshot, selectedThing, true, settings, currentTick, out detail);
            }
            x += 54f + gap;

            Rect offBtn = new Rect(x, btnY, 54f, btnH);
            if (PowerUiDraw.Button(offBtn, "PGM_Mgmt_Action_Off".Translate(), b.SwitchIsOn == false && usable, false, usable))
            {
                string detail;
                management.ManualSwitch(mapSnapshot, selectedThing, false, settings, currentTick, out detail);
            }
            x += 54f + gap;

            if (rect.xMax - x >= 88f)
            {
                Rect groupBtn = new Rect(x, btnY, 80f, btnH);
                if (PowerUiDraw.Button(groupBtn, "PGM_Mgmt_Action_Group".Translate()))
                    OpenGroupMenu(selectedThing, management, currentTick);
                x += 80f + gap;
            }

            if (b.IsConsumer && rect.xMax - x >= 90f)
            {
                Rect shedBtn = new Rect(x, btnY, 82f, btnH);
                string shedLabel = sheddable
                    ? (string)"PGM_Mgmt_Action_Protect".Translate()
                    : (string)"PGM_Mgmt_Action_Sheddable".Translate();
                if (PowerUiDraw.Button(shedBtn, shedLabel, sheddable, false, usable || sheddable))
                    management.SetSheddable(selectedThing, !sheddable, currentTick);
                x += 82f + gap;

                if (sheddable && rect.xMax - x >= 68f)
                {
                    Rect prioBtn = new Rect(x, btnY, 60f, btnH);
                    if (PowerUiDraw.Button(prioBtn, "P" + priority))
                        OpenPriorityMenu(selectedThing, management, currentTick);
                    x += 60f + gap;
                }
            }

            if (rect.xMax - x >= 92f)
            {
                Rect diagBtn = new Rect(x, btnY, 88f, btnH);
                string diagLabel = excludedDiag
                    ? (string)"PGM_Mgmt_Action_AllowDiagnostics".Translate()
                    : (string)"PGM_Mgmt_Action_Diagnostics".Translate();
                if (PowerUiDraw.Button(diagBtn, diagLabel, excludedDiag))
                    management.SetExcludedFromDiagnostics(selectedThing, !excludedDiag, currentTick);
                x += 88f + gap;
            }

            if (rect.xMax - x >= 90f)
            {
                string group = record != null ? record.Group : string.Empty;
                string statusLine = "PGM_Detail_Management".Translate(
                    CompatibilityRegistry.ControlLabelKey(support).Translate(),
                    "P" + priority,
                    string.IsNullOrEmpty(group) ? (string)"PGM_Group_None".Translate() : group);
                PowerUiDraw.Label(new Rect(x + 4f, rect.y, rect.xMax - x - 10f, rect.height),
                    statusLine, GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
            }
        }

        private static void OpenPriorityMenu(Building building, PowerManagementController management, int now)
        {
            var opts = new List<FloatMenuOption>(PowerManagementRegistry.MaxPriority);
            for (int p = PowerManagementRegistry.MinPriority; p <= PowerManagementRegistry.MaxPriority; p++)
            {
                int captured = p;
                opts.Add(new FloatMenuOption("P" + captured, delegate
                {
                    management.SetPriority(building, captured, now);
                }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static void OpenGroupMenu(Building building, PowerManagementController management, int now)
        {
            PowerManagementRegistry registry = management.Registry;
            var opts = new List<FloatMenuOption>(registry.GroupCount + 1)
            {
                new FloatMenuOption("PGM_Group_None".Translate(),
                    delegate { management.SetGroup(building, "", now); })
            };
            for (int i = 0; i < registry.GroupCount; i++)
            {
                string g = registry.Groups[i];
                opts.Add(new FloatMenuOption(g, delegate { management.SetGroup(building, g, now); }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static InspectorRole ResolvePrimaryRole(BuildingPowerSnapshot b)
        {
            if (b.IsBattery && !b.IsGenerator && !b.IsConsumer)
                return InspectorRole.Battery;
            if (b.IsGenerator && !b.IsConsumer)
                return InspectorRole.Generator;
            if (b.IsBattery && b.CapacityWattDays > 0f && b.ConsumptionWatts <= 0.01f && b.GenerationWatts <= 0.01f)
                return InspectorRole.Battery;
            if (b.IsGenerator && b.GenerationWatts >= b.ConsumptionWatts)
                return InspectorRole.Generator;
            return InspectorRole.Consumer;
        }

        private static Color RoleAccent(InspectorRole role, BuildingPowerSnapshot b,
            PowerGridSnapshot snap, PowerGridManagerSettings settings)
        {
            PowerNetworkSnapshot net = FindNetwork(snap, b.NetworkId);
            string unusedText;
            Color col;
            string unusedSym;
            ResolveRoleStatus(role, b, net, settings, out unusedText, out col, out unusedSym);
            return col;
        }

        private static string RoleBadgeLabel(InspectorRole role)
        {
            switch (role)
            {
                case InspectorRole.Generator:
                    return "PGM_Inspector_RoleGenerator".Translate();
                case InspectorRole.Battery:
                    return "PGM_Inspector_RoleBattery".Translate();
                default:
                    return "PGM_Inspector_RoleConsumer".Translate();
            }
        }

        private static void ResolveRoleStatus(
            InspectorRole role,
            BuildingPowerSnapshot b,
            PowerNetworkSnapshot net,
            PowerGridManagerSettings settings,
            out string label,
            out Color color,
            out string symbol)
        {
            if (b.HasUnsupportedComponent)
            {
                label = "PGM_RowStatus_Unsupported".Translate();
                color = PowerUiDraw.Warn;
                symbol = "?";
                return;
            }

            if (b.Connection == PowerConnectionState.NoNetwork)
            {
                label = "PGM_Connection_NoNetwork".Translate();
                color = PowerUiDraw.NoticeColor;
                symbol = "!";
                return;
            }

            if (b.Connection == PowerConnectionState.MembershipPending)
            {
                label = "PGM_Connection_Pending".Translate();
                color = PowerUiDraw.Warn;
                symbol = "?";
                return;
            }

            if (role == InspectorRole.Battery)
            {
                float usable = SumBuildingAvailableWattDays(b);
                if (b.StoredWattDays - usable > 0.02f)
                {
                    label = "PGM_Inspector_BatteryStunned".Translate();
                    color = PowerUiDraw.NoticeColor;
                    symbol = "!!";
                    return;
                }
                if (net != null && net.NetWatts < -0.01f && b.StoredWattDays > 0.005f)
                {
                    label = "PGM_Status_BatteryDischarging".Translate();
                    float frac = b.CapacityWattDays > 0f ? b.StoredWattDays / b.CapacityWattDays : 0f;
                    color = frac <= settings.LowBatteryWarningFraction ? PowerUiDraw.NoticeColor : PowerUiDraw.Warn;
                    symbol = "\u25BC";
                    return;
                }
                if (net != null && net.NetWatts > 0.01f && b.CapacityWattDays - b.StoredWattDays > 0.01f)
                {
                    label = "PGM_Status_BatteryCharging".Translate();
                    color = PowerUiDraw.AccentCyan;
                    symbol = "+";
                    return;
                }
                if (b.CapacityWattDays > 0f && b.StoredWattDays >= b.CapacityWattDays - 0.02f)
                {
                    label = "PGM_Inspector_BatteryFull".Translate();
                    color = PowerUiDraw.Good;
                    symbol = "\u25CF";
                    return;
                }
                if (b.StoredWattDays <= 0.005f)
                {
                    label = "PGM_Inspector_BatteryDepleted".Translate();
                    color = PowerUiDraw.NoticeColor;
                    symbol = "!";
                    return;
                }
                label = "PGM_Status_Stable".Translate();
                color = PowerUiDraw.Good;
                symbol = "\u25CF";
                return;
            }

            if (role == InspectorRole.Generator)
            {
                if (b.IsFlickable && b.SwitchIsOn == false)
                {
                    label = "PGM_RowStatus_SwitchedOff".Translate();
                    color = PowerUiDraw.TextSecondary;
                    symbol = "\u2013";
                    return;
                }
                if (b.GenerationWatts > 0.01f)
                {
                    label = "PGM_Inspector_GenOnline".Translate();
                    color = PowerUiDraw.Good;
                    symbol = "\u25CF";
                    return;
                }
                label = "PGM_Inspector_GenIdle".Translate();
                color = PowerUiDraw.Warn;
                symbol = "\u25B2";
                return;
            }

            if (b.IsFlickable && b.SwitchIsOn == false)
            {
                label = "PGM_RowStatus_SwitchedOff".Translate();
                color = PowerUiDraw.TextSecondary;
                symbol = "\u2013";
                return;
            }
            if (b.HasOffComponents)
            {
                label = "PGM_RowStatus_PoweredDown".Translate();
                color = PowerUiDraw.NoticeColor;
                symbol = "!";
                return;
            }
            if (PowerGridUtilities.IsHighConsumption(b, settings.HighConsumptionWatts))
            {
                label = "PGM_RowStatus_HighConsumption".Translate();
                color = PowerUiDraw.Warn;
                symbol = "\u25B2";
                return;
            }
            label = "PGM_RowStatus_Ok".Translate();
            color = PowerUiDraw.Good;
            symbol = "\u25CF";
        }

        private static PowerNetworkSnapshot FindNetwork(PowerGridSnapshot snap, int? networkId)
        {
            if (snap == null || !networkId.HasValue) return null;
            for (int i = 0; i < snap.Networks.Count; i++)
            {
                if (snap.Networks[i].Id == networkId.Value)
                    return snap.Networks[i];
            }
            return null;
        }

        private static float SumBuildingAvailableWattDays(BuildingPowerSnapshot b)
        {
            float sum = 0f;
            for (int i = 0; i < b.Components.Count; i++)
            {
                if (b.Components[i].Behavior == PowerBehavior.Battery)
                    sum += b.Components[i].AvailableStoredWattDays;
            }
            return sum;
        }

        private static float? ReadBatteryEfficiency(BuildingPowerSnapshot b)
        {
            for (int i = 0; i < b.Components.Count; i++)
            {
                if (b.Components[i].Behavior == PowerBehavior.Battery && b.Components[i].ChargeEfficiency.HasValue)
                    return b.Components[i].ChargeEfficiency;
            }
            return null;
        }

        private static string TraderStateText(BuildingPowerSnapshot b)
        {
            bool anyTrader = false;
            bool anyOn = false;
            bool anyOff = false;
            for (int i = 0; i < b.Components.Count; i++)
            {
                PowerComponentSnapshot c = b.Components[i];
                if (c.Behavior != PowerBehavior.Trader) continue;
                anyTrader = true;
                if (c.PowerOn == true) anyOn = true;
                else if (c.PowerOn == false) anyOff = true;
            }
            if (!anyTrader) return "PGM_Value_NotApplicable".Translate();
            if (anyOn && anyOff) return "PGM_TraderState_Mixed".Translate();
            return anyOn ? "PGM_TraderState_On".Translate() : "PGM_TraderState_Off".Translate();
        }

        private static string NetworkSummaryText(BuildingPowerSnapshot b, PowerNetworkSnapshot net)
        {
            if (b.HasMultipleNetworks)
                return "PGM_RowStatus_MultipleNetworks".Translate();
            if (b.NetworkId.HasValue)
            {
                if (net != null)
                {
                    return "PGM_Network_Number".Translate(b.NetworkId.Value)
                        + " (" + PowerFormatting.FormatWattsSigned(net.NetWatts) + ")";
                }
                return "PGM_Network_Number".Translate(b.NetworkId.Value);
            }
            if (b.Connection == PowerConnectionState.MembershipPending)
                return "PGM_Connection_Pending".Translate();
            return "PGM_Connection_NoNetwork".Translate();
        }

        private static string FormatNetworkEndurance(BatteryForecastResult forecast)
        {
            if (forecast == null) return "PGM_Value_Unknown".Translate();
            switch (forecast.Kind)
            {
                case BatteryForecastKind.Depleting:
                    return forecast.TicksToEmpty.HasValue
                        ? (string)"PGM_Endurance_Depleting".Translate(PowerFormatting.FormatDuration(forecast.TicksToEmpty.Value))
                        : (string)"PGM_Endurance_Depleting".Translate(PowerFormatting.FormatDuration(0));
                case BatteryForecastKind.Charging:
                    return forecast.TicksToFull.HasValue
                        ? (string)"PGM_Endurance_Charging".Translate(PowerFormatting.FormatDuration(forecast.TicksToFull.Value))
                        : (string)"PGM_Forecast_Full".Translate();
                case BatteryForecastKind.Balanced:
                    return "PGM_Endurance_Balanced".Translate();
                default:
                    return "PGM_Endurance_NoBatteries".Translate();
            }
        }
    }
}
