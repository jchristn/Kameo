namespace Kameo.Desktop
{
    using System;
    using Avalonia.Controls;
    using Avalonia.Input;

    internal static class NativeMenus
    {
        internal static NativeMenuItem Item(string label, Action action, Key? key = null)
        {
            NativeMenuItem item = new NativeMenuItem(label);
            if (key != null) item.Gesture = new KeyGesture(key.Value, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
            item.Click += (_, _) => action();
            return item;
        }

        internal static NativeMenuItem Submenu(string label, params NativeMenuItemBase[] items)
        {
            NativeMenu menu = new NativeMenu();
            foreach (NativeMenuItemBase item in items) menu.Add(item);
            return new NativeMenuItem(label) { Menu = menu };
        }

        internal static NativeMenuItem Edit(Window window)
        {
            TextBox? Focused() => window.FocusManager?.GetFocusedElement() as TextBox;
            return Submenu("Edit",
                Item("Undo", () => Focused()?.Undo(), Key.Z),
                Item("Redo", () => Focused()?.Redo()),
                new NativeMenuItemSeparator(),
                Item("Cut", () => Focused()?.Cut(), Key.X),
                Item("Copy", () => Focused()?.Copy(), Key.C),
                Item("Paste", () => Focused()?.Paste(), Key.V),
                Item("Select All", () => Focused()?.SelectAll(), Key.A));
        }
    }
}
