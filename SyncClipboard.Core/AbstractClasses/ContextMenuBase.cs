using SyncClipboard.Core.Interfaces;
using System.Collections;
using System.Collections.Specialized;
using WinRT;

namespace SyncClipboard.Core.AbstractClasses
{
    public abstract class ContextMenuBase : IContextMenu
    {
        protected abstract void InsertToggleMenuItem(int index, ToggleMenuItem menuitem);
        protected abstract void InsertMenuItem(int index, MenuItem menuitem);
        protected abstract void InsertSeparator(int index);
        protected abstract int MenuItemsCount();

        private record class GroupInfo
        {
            public int Index { get; set; }
            public bool Created { get; set; } = false;
        }

        private readonly OrderedDictionary _groupInfos = new();

        public void AddMenuItemGroup(MenuItem[] menuItems, string? group = null)
        {
            group ??= Guid.NewGuid().ToString();

            foreach (var item in menuItems)
            {
                AddSingleMenuItem(item, group);
            }
        }

        private void AddSingleMenuItem(MenuItem menuitem, string group = "default")
        {
            if (menuitem is ToggleMenuItem toggleItem)
            {
                InsertToggleMenuItem(GetIndexAndAutoIncrease(group), toggleItem);
            }
            else
            {
                InsertMenuItem(GetIndexAndAutoIncrease(group), menuitem);
            }
        }

        private int GetIndexAndAutoIncrease(string group)
        {
            if (!_groupInfos.Contains(group))
            {
                CreateGroup(group);
            }

            var info = (GroupInfo)_groupInfos[group]!;
            if (!info.Created)
            {
                if (info.Index == 0)
                {
                    if (MenuItemsCount() != 0)
                        AddSeparator(info.Index);
                }
                else
                {
                    AddSeparator(info.Index++);
                }
            }

            int step = info.Created ? 1 : 2;
            info.Created = true;
            bool find = false;
            foreach (DictionaryEntry groupInfo in _groupInfos)
            {
                if (find)
                    groupInfo.Value.As<GroupInfo>().Index += step;
                if (groupInfo.Key.As<string>() == group)
                    find = true;
            }

            return info.Index++;
        }

        public void AddMenuItem(MenuItem item, string? group)
        {
            group ??= "Default";
            AddSingleMenuItem(item, group);
        }

        private void AddSeparator(int index)
        {
            InsertSeparator(index);
        }

        private void CreateGroup(string group)
        {
            if (_groupInfos.Contains(group))
            {
                return;
            }

            _groupInfos.Add(group, new GroupInfo { Index = MenuItemsCount() });
        }
    }
}
