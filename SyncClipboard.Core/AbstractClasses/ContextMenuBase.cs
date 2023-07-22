using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.AbstractClasses
{
    public abstract class ContextMenuBase : IContextMenu
    {
        protected abstract void InsertToggleMenuItem(int index, ToggleMenuItem menuitem);
        protected abstract void InsertMenuItem(int index, MenuItem menuitem);
        protected abstract void InsertSeparator(int index);

        private int _index = 0;
        public void AddMenuItemGroup(MenuItem[] menuItems, bool reverse = false)
        {
            if (!reverse)
                AddSeparator(reverse);

            var items = reverse ? menuItems.Reverse() : menuItems;
            foreach (var item in items)
            {
                AddSingleMenuItem(item, reverse);
            }

            if (reverse)
                AddSeparator(reverse);
        }

        private void AddSingleMenuItem(MenuItem menuitem, bool reverse = false)
        {
            if (menuitem is ToggleMenuItem toggleItem)
            {
                InsertToggleMenuItem(GetIndexAndAutoIncrease(reverse), toggleItem);
            }
            else
            {
                InsertMenuItem(GetIndexAndAutoIncrease(reverse), menuitem);
            }
        }

        private int GetIndexAndAutoIncrease(bool reverse)
        {
            if (reverse)
            {
                return _index;
            }
            return _index++;
        }

        public void AddMenuItem(MenuItem item, bool reverse = false)
        {
            AddMenuItemGroup(new MenuItem[] { item }, reverse);
        }

        private void AddSeparator(bool reverse)
        {
            if (_index != 0)
            {
                InsertSeparator(GetIndexAndAutoIncrease(reverse));
            }
        }
    }
}
