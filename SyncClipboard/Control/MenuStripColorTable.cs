using System.Windows.Forms;
using System.Drawing;

namespace SyncClipboard.Control
{
    public class MenuStripColorTable : ProfessionalColorTable
    {
        private readonly static Color BackgroundColor = Color.FromArgb(0xf2, 0xf2, 0xf2);
        private readonly static Color SelectedItemColor = Color.FromArgb(0x91, 0xc9, 0xf7);

        // 整体背景颜色
        public override Color ToolStripDropDownBackground { get => BackgroundColor; }

        // // 每个item边框
        public override Color MenuItemBorder { get => BackgroundColor; }

        // 菜单整体边框
        public override Color MenuBorder { get => Color.LightGray; }

        // 间隔线颜色
        public override Color SeparatorDark { get => Color.LightGray; }

        // 为true时的checkbox边框颜色
        public override Color ButtonSelectedBorder { get => SelectedItemColor; }

        // 为true时的checkbox背景颜色
        public override Color CheckBackground { get => BackgroundColor; }

        // 当鼠标移动到为true时的checkbox时，checkbox的背景颜色
        public override Color CheckSelectedBackground { get => SelectedItemColor; }

        // 当鼠标移动到为true时的checkbox并按下时，checkbox的背景颜色
        public override Color CheckPressedBackground { get => SelectedItemColor; }

        #region 左侧图标列背景颜色
        public override Color ImageMarginGradientBegin { get => BackgroundColor; }
        public override Color ImageMarginGradientMiddle { get => BackgroundColor; }
        public override Color ImageMarginGradientEnd { get => BackgroundColor; }
        #endregion

        #region 鼠标选中时的颜色
        public override Color MenuItemSelectedGradientEnd { get => SelectedItemColor; }
        public override Color MenuItemSelectedGradientBegin { get => SelectedItemColor; }
        #endregion
    }
}