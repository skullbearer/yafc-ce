﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using SDL2;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class ProjectPageSettingsPanel : PseudoScreen {
    private static readonly JsonSerializerOptions jsonSerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ProjectPage? editingPage;
    private string name;
    private FactorioObject? icon;
    private readonly Action<string, FactorioObject?>? callback;

    private ProjectPageSettingsPanel(ProjectPage? editingPage, Action<string, FactorioObject?>? callback) {
        this.editingPage = editingPage;
        name = editingPage?.name ?? "";
        icon = editingPage?.icon;
        this.callback = callback;
    }

    private void Build(ImGui gui, Action<FactorioObject?> setIcon) {
        _ = gui.BuildTextInput(name, out name, "Input name", setInitialFocus: editingPage == null);
        if (gui.BuildFactorioObjectButton(icon, new ButtonDisplayStyle(4f, MilestoneDisplay.None, SchemeColor.Grey) with { UseScaleSetting = false }) == Click.Left) {
            SelectSingleObjectPanel.Select(Database.objects.all, "Select icon", setIcon);
        }

        if (icon == null && gui.isBuilding) {
            gui.DrawText(gui.lastRect, "And select icon", RectAlignment.Middle);
        }
    }

    public static void Show(ProjectPage? page, Action<string, FactorioObject?>? callback = null) => _ = MainScreen.Instance.ShowPseudoScreen(new ProjectPageSettingsPanel(page, callback));

    public override void Build(ImGui gui) {
        gui.spacing = 3f;
        BuildHeader(gui, editingPage == null ? "Create new page" : "Edit page icon and name");
        Build(gui, s => {
            icon = s;
            Rebuild();
        });

        using (gui.EnterRow(0.5f, RectAllocator.RightRow)) {
            if (editingPage == null && gui.BuildButton("Create", active: !string.IsNullOrEmpty(name))) {
                ReturnPressed();
            }

            if (editingPage != null && gui.BuildButton("OK", active: !string.IsNullOrEmpty(name))) {
                ReturnPressed();
            }

            if (gui.BuildButton("Cancel", SchemeColor.Grey)) {
                Close();
            }

            if (editingPage != null && gui.BuildButton("Other tools", SchemeColor.Grey, active: !string.IsNullOrEmpty(name))) {
                gui.ShowDropDown(OtherToolsDropdown);
            }

            gui.allocator = RectAllocator.LeftRow;
            if (editingPage != null && gui.BuildRedButton("Delete page")) {
                if (editingPage.canDelete) {
                    Project.current.RemovePage(editingPage);
                }
                else {
                    // Only hide if the (singleton) page cannot be deleted
                    MainScreen.Instance.ClosePage(editingPage.guid);
                }
                Close();
            }
        }
    }

    protected override void ReturnPressed() {
        if (string.IsNullOrEmpty(name)) {
            // Prevent closing with an empty name
            return;
        }
        if (editingPage is null) {
            callback?.Invoke(name, icon);
        }
        else if (editingPage.name != name || editingPage.icon != icon) {
            editingPage.RecordUndo(true).name = name!; // null-forgiving: The button is disabled if name is null or empty.
            editingPage.icon = icon;
        }
        Close();
    }

    private void OtherToolsDropdown(ImGui gui) {
        if (editingPage!.guid != MainScreen.SummaryGuid && gui.BuildContextMenuButton("Duplicate page")) { // null-forgiving: This dropdown is not shown when editingPage is null.
            _ = gui.CloseDropdown();
            var project = editingPage.owner;
            if (ClonePage(editingPage) is { } serializedCopy) {
                serializedCopy.icon = icon;
                serializedCopy.name = name;
                project.RecordUndo().pages.Add(serializedCopy);
                MainScreen.Instance.SetActivePage(serializedCopy);
                Close();
            }
        }

        if (editingPage.guid != MainScreen.SummaryGuid && gui.BuildContextMenuButton("Share (export string to clipboard)")) {
            _ = gui.CloseDropdown();
            var data = JsonUtils.SaveToJson(editingPage);
            using MemoryStream targetStream = new MemoryStream();
            using (DeflateStream compress = new DeflateStream(targetStream, CompressionLevel.Optimal, true)) {
                using (BinaryWriter writer = new BinaryWriter(compress, Encoding.UTF8, true)) {
                    // write some magic chars and version as a marker
                    writer.Write("YAFC\nProjectPage\n".AsSpan());
                    writer.Write(YafcLib.version.ToString().AsSpan());
                    writer.Write("\n\n\n".AsSpan());
                }
                data.CopyTo(compress);
            }
            string encoded = Convert.ToBase64String(targetStream.GetBuffer(), 0, (int)targetStream.Length);
            _ = SDL.SDL_SetClipboardText(encoded);
        }

        if (editingPage == MainScreen.Instance.activePage && gui.BuildContextMenuButton("Make full page screenshot")) {
            // null-forgiving: editingPage is not null, so neither is activePage, and activePage and activePageView become null or not-null together. (see MainScreen.ChangePage)
            var screenshot = MainScreen.Instance.activePageView!.GenerateFullPageScreenshot();
            _ = new ImageSharePanel(screenshot, editingPage.name);
            _ = gui.CloseDropdown();
        }

        if (gui.BuildContextMenuButton("Export calculations (to clipboard)")) {
            ExportPage(editingPage);
            _ = gui.CloseDropdown();
        }
    }

    public static ProjectPage? ClonePage(ProjectPage page) {
        ErrorCollector collector = new ErrorCollector();
        var serializedCopy = JsonUtils.Copy(page, page.owner, collector);
        if (collector.severity > ErrorSeverity.None) {
            ErrorListPanel.Show(collector);
        }

        serializedCopy?.GenerateNewGuid();
        return serializedCopy;
    }

    private class ExportRow(RecipeRow row) {
        public ExportRecipe? Header { get; } = row.recipe is null ? null : new ExportRecipe(row);
        public IEnumerable<ExportRow> Children { get; } = row.subgroup?.recipes.Select(r => new ExportRow(r)) ?? [];
    }

    private class ExportRecipe {
        public string Recipe { get; }
        public string Building { get; }
        public double BuildingCount { get; }
        public IEnumerable<string> Modules { get; }
        public string? Beacon { get; }
        public int BeaconCount { get; }
        public IEnumerable<string> BeaconModules { get; }
        public ExportMaterial Fuel { get; }
        public IEnumerable<ExportMaterial> Inputs { get; }
        public IEnumerable<ExportMaterial> Outputs { get; }

        public ExportRecipe(RecipeRow row) {
            Recipe = row.recipe.name;
            Building = row.entity?.name ?? "<No building selected>";
            BuildingCount = row.buildingCount;
            Fuel = new ExportMaterial(row.fuel?.name ?? "<No fuel selected>", row.FuelInformation.Amount);
            Inputs = row.Ingredients.Select(i => new ExportMaterial(i.Goods?.name ?? "Recipe disabled", i.Amount));
            Outputs = row.Products.Select(p => new ExportMaterial(p.Goods?.name ?? "Recipe disabled", p.Amount));
            Beacon = row.usedModules.beacon?.name;
            BeaconCount = row.usedModules.beaconCount;

            if (row.usedModules.modules is null) {
                Modules = BeaconModules = [];
            }
            else {
                List<string> modules = [];
                List<string> beaconModules = [];

                foreach (var (module, count, isBeacon) in row.usedModules.modules) {
                    if (isBeacon) {
                        beaconModules.AddRange(Enumerable.Repeat(module.name, count));
                    }
                    else {
                        modules.AddRange(Enumerable.Repeat(module.name, count));
                    }
                }

                Modules = modules;
                BeaconModules = beaconModules;
            }
        }
    }

    private class ExportMaterial(string name, double countPerSecond) {
        public string Name { get; } = name;
        public double CountPerSecond { get; } = countPerSecond;
    }

    private static void ExportPage(ProjectPage page) {
        using MemoryStream stream = new MemoryStream();
        using Utf8JsonWriter writer = new Utf8JsonWriter(stream);
        ProductionTable pageContent = ((ProductionTable)page.content);

        JsonSerializer.Serialize(stream, pageContent.recipes.Select(rr => new ExportRow(rr)), jsonSerializerOptions);
        _ = SDL.SDL_SetClipboardText(Encoding.UTF8.GetString(stream.GetBuffer()));
    }

    public static void LoadProjectPageFromClipboard() {
        ErrorCollector collector = new ErrorCollector();
        var project = Project.current;
        ProjectPage? page = null;
        try {
            string text = SDL.SDL_GetClipboardText();
            byte[] compressedBytes = Convert.FromBase64String(text.Trim());
            using DeflateStream deflateStream = new DeflateStream(new MemoryStream(compressedBytes), CompressionMode.Decompress);
            using MemoryStream ms = new MemoryStream();
            deflateStream.CopyTo(ms);
            byte[] bytes = ms.GetBuffer();
            int index = 0;

#pragma warning disable IDE0078 // Use pattern matching: False positive detection that changes code behavior
            if (DataUtils.ReadLine(bytes, ref index) != "YAFC" || DataUtils.ReadLine(bytes, ref index) != "ProjectPage") {
#pragma warning restore IDE0078
                throw new InvalidDataException();
            }

            Version version = new Version(DataUtils.ReadLine(bytes, ref index) ?? "");
            if (version > YafcLib.version) {
                collector.Error("String was created with the newer version of YAFC (" + version + "). Data may be lost.", ErrorSeverity.Important);
            }

            _ = DataUtils.ReadLine(bytes, ref index); // reserved 1
            if (DataUtils.ReadLine(bytes, ref index) != "") // reserved 2 but this time it is required to be empty
{
                throw new NotSupportedException("Share string was created with future version of YAFC (" + version + ") and is incompatible");
            }

            page = JsonUtils.LoadFromJson<ProjectPage>(new ReadOnlySpan<byte>(bytes, index, (int)ms.Length - index), project, collector);
        }
        catch (Exception ex) {
            collector.Exception(ex, "Clipboard text does not contain valid YAFC share string", ErrorSeverity.Critical);
        }

        if (page != null) {
            var existing = project.FindPage(page.guid);
            if (existing != null) {
                MessageBox.Show((haveChoice, choice) => {
                    if (!haveChoice) {
                        return;
                    }

                    if (choice) {
                        project.RemovePage(existing);
                    }
                    else {
                        page.GenerateNewGuid();
                    }

                    project.RecordUndo().pages.Add(page);
                    MainScreen.Instance.SetActivePage(page);
                }, "Page already exists",
                "Looks like this page already exists with name '" + existing.name + "'. Would you like to replace it or import as copy?", "Replace", "Import as copy");
            }
            else {
                project.RecordUndo().pages.Add(page);
                MainScreen.Instance.SetActivePage(page);
            }
        }

        if (collector.severity > ErrorSeverity.None) {
            ErrorListPanel.Show(collector);
        }
    }
}
