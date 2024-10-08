﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using SDL2;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

// Contained draws the milestone like this
// +----+
// |    |
// |  +-+
// |  | |
// +--+-+
// Normal draws the milestone like this
// +----+
// |    |
// |    |
// |   +-+
// +---| |
//     +-+
public enum MilestoneDisplay {
    /// <summary>
    /// Draw the highest locked milestone centered on the lower-right corner of the base icon.
    /// </summary>
    Normal,
    /// <summary>
    /// Draw the highest locked milestone with its lower-right corner aligned with the lower-right corner of the base icon.
    /// </summary>
    Contained,
    /// <summary>
    /// Draw the highest milestone, regardless of (un)lock state, centered on the lower-right corner of the base icon.
    /// </summary>
    Always,
    /// <summary>
    /// Draw the highest milestone, regardless of (un)lock state, with its lower-right corner aligned with the lower-right corner of the base icon.
    /// </summary>
    ContainedAlways,
    /// <summary>
    /// Do not draw a milestone icon.
    /// </summary>
    None
}

public enum GoodsWithAmountEvent {
    None,
    LeftButtonClick,
    RightButtonClick,
    TextEditing,
}

public enum Click {
    None,
    Left,
    Right,
}

public static class ImmediateWidgets {
    /// <summary>Draws the icon belonging to a <see cref="FactorioObject"/>, or an empty box as a placeholder if no object is available.</summary>
    /// <param name="obj">Draw the icon for this object, or an empty box if this is <see langword="null"/>.</param>
    public static void BuildFactorioObjectIcon(this ImGui gui, FactorioObject? obj, IconDisplayStyle? displayStyle = null) {
        displayStyle ??= IconDisplayStyle.Default;
        if (obj == null) {
            gui.BuildIcon(Icon.Empty, displayStyle.Size, SchemeColor.BackgroundTextFaint);
            return;
        }

        var color = obj.IsAccessible() ? SchemeColor.Source : SchemeColor.SourceFaint;
        if (displayStyle.UseScaleSetting) {
            Rect rect = gui.AllocateRect(displayStyle.Size, displayStyle.Size, RectAlignment.Middle);
            gui.DrawIcon(rect.Expand(displayStyle.Size * (Project.current.preferences.iconScale - 1) / 2), obj.icon, color);
        }
        else {
            gui.BuildIcon(obj.icon, displayStyle.Size, color);
        }
        if (gui.isBuilding && displayStyle.MilestoneDisplay != MilestoneDisplay.None) {
            bool contain = (displayStyle.MilestoneDisplay & MilestoneDisplay.Contained) != 0;
            FactorioObject? milestone = Milestones.Instance.GetHighest(obj, displayStyle.MilestoneDisplay >= MilestoneDisplay.Always);
            if (milestone != null) {
                Vector2 psize = new Vector2(displayStyle.Size / 2f);
                var delta = contain ? psize : psize / 2f;
                Rect milestoneIcon = new Rect(gui.lastRect.BottomRight - delta, psize);
                var icon = milestone == Database.voidEnergy ? DataUtils.HandIcon : milestone.icon;
                gui.DrawIcon(milestoneIcon, icon, color);
            }
        }
    }

    public static bool BuildFloatInput(this ImGui gui, DisplayAmount amount, TextBoxDisplayStyle displayStyle, bool setInitialFocus = false) {
        if (gui.BuildTextInput(DataUtils.FormatAmount(amount.Value, amount.Unit), out string newText, null, displayStyle, true, setInitialFocus)
            && DataUtils.TryParseAmount(newText, out double newValue, amount.Unit)) {
            amount.Value = (float)newValue;
            return true;
        }

        return false;
    }

    public static Click BuildFactorioObjectButtonBackground(this ImGui gui, Rect rect, FactorioObject? obj, SchemeColor bgColor = SchemeColor.None,
        ObjectTooltipOptions tooltipOptions = default) {

        SchemeColor overColor;

        if (bgColor == SchemeColor.None) {
            overColor = SchemeColor.Grey;
        }
        else {
            overColor = bgColor + 1;
        }

        if (MainScreen.Instance.IsSameObjectHovered(gui, obj)) {
            bgColor = overColor;
        }

        var evt = gui.BuildButton(rect, bgColor, overColor, button: 0);

        if (evt == ButtonEvent.MouseOver && obj != null) {
            MainScreen.Instance.ShowTooltip(obj, gui, rect, tooltipOptions);
        }
        else if (evt == ButtonEvent.Click) {
            if (gui.actionParameter == SDL.SDL_BUTTON_MIDDLE && obj != null) {
                if (obj.showInExplorers) {
                    if (obj is Goods goods && obj.IsAccessible()) {
                        NeverEnoughItemsPanel.Show(goods);
                    }
                    else {
                        DependencyExplorer.Show(obj);
                    }
                }
            }
            else if (gui.actionParameter == SDL.SDL_BUTTON_LEFT) {
                return Click.Left;
            }
            else if (gui.actionParameter == SDL.SDL_BUTTON_RIGHT) {
                return Click.Right;
            }
        }

        return Click.None;
    }

    /// <summary>Draws a button displaying the icon belonging to a <see cref="FactorioObject"/>, or an empty box as a placeholder if no object is available.</summary>
    /// <param name="obj">Draw the icon for this object, or an empty box if this is <see langword="null"/>.</param>
    public static Click BuildFactorioObjectButton(this ImGui gui, FactorioObject? obj, ButtonDisplayStyle displayStyle, ObjectTooltipOptions tooltipOptions = default) {
        gui.BuildFactorioObjectIcon(obj, displayStyle);
        return gui.BuildFactorioObjectButtonBackground(gui.lastRect, obj, displayStyle.BackgroundColor, tooltipOptions);
    }

    public static Click BuildFactorioObjectButtonWithText(this ImGui gui, FactorioObject? obj, string? extraText = null, IconDisplayStyle? iconDisplayStyle = null) {
        iconDisplayStyle ??= IconDisplayStyle.Default;
        using (gui.EnterRow()) {
            gui.BuildFactorioObjectIcon(obj, iconDisplayStyle);
            var color = gui.textColor;
            if (obj != null && !obj.IsAccessible()) {
                color += 1;
            }

            if (Project.current.preferences.favorites.Contains(obj!)) { // null-forgiving: non-nullable collections are happy to report they don't contain null values.
                gui.BuildIcon(Icon.StarFull, 1f);
            }

            if (extraText != null) {
                gui.AllocateSpacing();
                gui.allocator = RectAllocator.RightRow;
                gui.BuildText(extraText, TextBlockDisplayStyle.Default(color));
            }
            _ = gui.RemainingRow();
            gui.BuildText(obj == null ? "None" : obj.locName, TextBlockDisplayStyle.WrappedText with { Color = color });
        }

        return gui.BuildFactorioObjectButtonBackground(gui.lastRect, obj);
    }

    public static bool BuildInlineObjectList<T>(this ImGui gui, IEnumerable<T> list, IComparer<T> ordering, string header, [NotNullWhen(true)] out T? selected, int maxCount = 10,
        Predicate<T>? checkMark = null, Func<T, string>? extra = null) where T : FactorioObject {
        gui.BuildText(header, Font.productionTableHeader);
        IEnumerable<T> sortedList;

        if (ordering == DataUtils.AlreadySortedRecipe) {
            sortedList = list.AsEnumerable();
        }
        else {
            sortedList = list.OrderBy(e => e, ordering ?? DataUtils.DefaultOrdering);
        }

        selected = null;

        foreach (var elem in sortedList.Take(maxCount)) {
            string? extraText = extra?.Invoke(elem);

            if (gui.BuildFactorioObjectButtonWithText(elem, extraText) == Click.Left) {
                selected = elem;
            }

            if (checkMark != null && gui.isBuilding && checkMark(elem)) {
                gui.DrawIcon(Rect.Square(gui.lastRect.Right - 1f, gui.lastRect.Center.Y, 1.5f), Icon.Check, SchemeColor.Green);
            }
        }

        return selected != null;
    }

    public static void BuildInlineObjectListAndButton<T>(this ImGui gui, ICollection<T> list, IComparer<T> ordering, Action<T> selectItem, string header,
        int count = 6, bool multiple = false, Predicate<T>? checkMark = null, Func<T, string>? extra = null) where T : FactorioObject {

        using (gui.EnterGroup(default, RectAllocator.Stretch)) {
            if (gui.BuildInlineObjectList(list, ordering, header, out var selected, count, checkMark, extra)) {
                selectItem(selected);
                if (!multiple || !InputSystem.Instance.control) {
                    _ = gui.CloseDropdown();
                }
            }

            if (list.Count > count && gui.BuildButton("See full list") && gui.CloseDropdown()) {
                if (multiple) {
                    SelectMultiObjectPanel.Select(list, header, selectItem, ordering, checkMark);
                }
                else {
                    SelectSingleObjectPanel.Select(list, header, selectItem, ordering);
                }
            }
        }
    }

    public static void BuildInlineObjectListAndButtonWithNone<T>(this ImGui gui, ICollection<T> list, IComparer<T> ordering, Action<T?> selectItem, string header,
        int count = 6, Func<T, string>? extra = null) where T : FactorioObject {

        using (gui.EnterGroup(default, RectAllocator.Stretch)) {
            if (gui.BuildInlineObjectList(list, ordering, header, out var selected, count, null, extra)) {
                selectItem(selected);
                _ = gui.CloseDropdown();
            }
            if (gui.BuildRedButton("Clear") && gui.CloseDropdown()) {
                selectItem(null);
            }

            if (list.Count > count && gui.BuildButton("See full list") && gui.CloseDropdown()) {
                SelectSingleObjectPanel.SelectWithNone(list, header, selectItem, ordering);
            }
        }
    }

    /// <summary>Draws a button displaying the icon belonging to a <see cref="FactorioObject"/>, or an empty box as a placeholder if no object is available.
    /// Also draws a label under the button, containing the supplied <paramref name="amount"/>.</summary>
    /// <param name="goods">Draw the icon for this object, or an empty box if this is <see langword="null"/>.</param>
    /// <param name="amount">Display this value and unit.</param>
    /// <param name="useScale">If <see langword="true"/>, this icon will be displayed at <see cref="ProjectPreferences.iconScale"/>, instead of at 100% scale.</param>
    public static Click BuildFactorioObjectWithAmount(this ImGui gui, FactorioObject? goods, DisplayAmount amount, ButtonDisplayStyle buttonDisplayStyle, TextBlockDisplayStyle? textDisplayStyle = null, ObjectTooltipOptions tooltipOptions = default) {
        textDisplayStyle ??= new(Alignment: RectAlignment.Middle);
        using (gui.EnterFixedPositioning(3f, 3f, default)) {
            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;
            Click clicked = gui.BuildFactorioObjectButton(goods, buttonDisplayStyle, tooltipOptions);

            if (goods != null) {
                gui.BuildText(DataUtils.FormatAmount(amount.Value, amount.Unit), textDisplayStyle);
                if (InputSystem.Instance.control && gui.BuildButton(gui.lastRect, SchemeColor.None, SchemeColor.Grey) == ButtonEvent.MouseOver) {
                    ShowPrecisionValueTooltip(gui, amount, goods);
                }
            }

            return clicked;
        }
    }

    public static void ShowPrecisionValueTooltip(ImGui gui, DisplayAmount amount, FactorioObject goods) {
        string text;
        switch (amount.Unit) {
            case UnitOfMeasure.PerSecond:
            case UnitOfMeasure.FluidPerSecond:
            case UnitOfMeasure.ItemPerSecond:
                string perSecond = DataUtils.FormatAmountRaw(amount.Value, 1f, "/s", DataUtils.PreciseFormat);
                string perMinute = DataUtils.FormatAmountRaw(amount.Value, 60f, "/m", DataUtils.PreciseFormat);
                string perHour = DataUtils.FormatAmountRaw(amount.Value, 3600f, "/h", DataUtils.PreciseFormat);
                text = perSecond + "\n" + perMinute + "\n" + perHour;

                if (goods is Item item) {
                    text += DataUtils.FormatAmount(MathF.Abs(item.stackSize / amount.Value), UnitOfMeasure.Second, "\n", " per stack");
                }

                break;
            default:
                text = DataUtils.FormatAmount(amount.Value, amount.Unit, precise: true);
                break;
        }
        gui.ShowTooltip(gui.lastRect, x => {
            _ = x.BuildFactorioObjectButtonWithText(goods);
            x.BuildText(text, TextBlockDisplayStyle.WrappedText);
        }, 10f);
    }

    /// <summary>Shows a dropdown containing the (partial) <paramref name="list"/> of elements, with an action for when an element is selected.</summary>
    /// <param name="count">Maximum number of elements in the list. If there are more another popup can be opened by the user to show the full list.</param>
    /// <param name="width">Width of the popup. Make sure the header text fits!</param>
    public static void BuildObjectSelectDropDown<T>(this ImGui gui, ICollection<T> list, IComparer<T> ordering, Action<T> selectItem, string header, float width = 20f,
        int count = 6, bool multiple = false, Predicate<T>? checkMark = null, Func<T, string>? extra = null) where T : FactorioObject

        => gui.ShowDropDown(imGui => imGui.BuildInlineObjectListAndButton(list, ordering, selectItem, header, count, multiple, checkMark, extra), width);

    /// <summary>Shows a dropdown containing the (partial) <paramref name="list"/> of elements, with an action for when an element is selected. An additional "Clear" or "None" option will also be displayed.</summary>
    /// <param name="count">Maximum number of elements in the list. If there are more another popup can be opened by the user to show the full list.</param>
    /// <param name="width">Width of the popup. Make sure the header text fits!</param>
    public static void BuildObjectSelectDropDownWithNone<T>(this ImGui gui, ICollection<T> list, IComparer<T> ordering, Action<T?> selectItem, string header, float width = 20f,
        int count = 6, Func<T, string>? extra = null) where T : FactorioObject

        => gui.ShowDropDown(imGui => imGui.BuildInlineObjectListAndButtonWithNone(list, ordering, selectItem, header, count, extra), width);

    /// <summary>Draws a button displaying the icon belonging to a <see cref="FactorioObject"/>, or an empty box as a placeholder if no object is available.
    /// Also draws an editable textbox under the button, containing the supplied <paramref name="amount"/>.</summary>
    /// <param name="obj">Draw the icon for this object, or an empty box if this is <see langword="null"/>.</param>
    /// <param name="useScale">If <see langword="true"/>, this icon will be displayed at <see cref="ProjectPreferences.iconScale"/>, instead of at 100% scale.</param>
    /// <param name="amount">Display this value and unit. If the user edits the value, the new value will be stored in <see cref="DisplayAmount.Value"/> before returning.</param>
    /// <param name="allowScroll">If <see langword="true"/>, the default, the user can adjust the value by using the scroll wheel while hovering over the editable text.
    /// If <see langword="false"/>, the scroll wheel will be ignored when hovering.</param>
    public static GoodsWithAmountEvent BuildFactorioObjectWithEditableAmount(this ImGui gui, FactorioObject? obj, DisplayAmount amount, ButtonDisplayStyle buttonDisplayStyle,
        bool allowScroll = true, ObjectTooltipOptions tooltipOptions = default) {

        using var group = gui.EnterGroup(default, RectAllocator.Stretch, spacing: 0f);
        group.SetWidth(3f);
        GoodsWithAmountEvent evt = (GoodsWithAmountEvent)gui.BuildFactorioObjectButton(obj, buttonDisplayStyle, tooltipOptions);

        if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.FactorioObjectInput)) {
            return GoodsWithAmountEvent.TextEditing;
        }

        if (allowScroll && gui.action == ImGuiAction.MouseScroll && gui.ConsumeEvent(gui.lastRect)) {
            float digit = MathF.Pow(10, MathF.Floor(MathF.Log10(amount.Value) - 2f));
            amount.Value = MathF.Round((amount.Value / digit) + gui.actionParameter) * digit;
            return GoodsWithAmountEvent.TextEditing;
        }

        return evt;
    }
}

/// <summary>
/// Represents an amount to be displayed to the user, and possibly edited.
/// </summary>
/// <param name="Value">The initial value to be displayed to the user.</param>
/// <param name="Unit">The <see cref="UnitOfMeasure"/> to be used when formatting <paramref name="Value"/> for display and when parsing user input.</param>
public record DisplayAmount(float Value, UnitOfMeasure Unit = UnitOfMeasure.None) {
    /// <summary>
    /// Gets or sets the value. This is either the value to be displayed or the value after modification by the user.
    /// </summary>
    public float Value { get; set; } = Value;

    /// <summary>
    /// Creates a new <see cref="DisplayAmount"/> for basic numeric display. <see cref="Unit"/> will be set to <see cref="UnitOfMeasure.None"/>.
    /// </summary>
    /// <param name="value">The initial value to be displayed to the user.</param>
    public static implicit operator DisplayAmount(float value) => new(value);
}
