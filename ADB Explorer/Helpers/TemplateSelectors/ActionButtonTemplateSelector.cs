using ADB_Explorer.Services;
using System;
using System.Windows;
using System.Windows.Controls;

namespace ADB_Explorer.Helpers
{
    public class ActionButtonTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ResetSettingTemplate { get; set; }
        public DataTemplate AnimationTipSettingTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                ResetCommand => ResetSettingTemplate,
                ShowAnimationTipCommand => AnimationTipSettingTemplate,
                _ => throw new NotImplementedException(),
            };
        }
    }
}
