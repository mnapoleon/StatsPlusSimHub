using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameReaderCommon;
using FormsControl = System.Windows.Forms.Control;
using WpfControl = System.Windows.Controls.Control;
using WpfGroupBox = System.Windows.Controls.GroupBox;
using WpfTabControl = System.Windows.Controls.TabControl;
using WpfTabItem = System.Windows.Controls.TabItem;

namespace SimHub.Plugins
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class PluginNameAttribute : Attribute
    {
        public PluginNameAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class PluginDescriptionAttribute : Attribute
    {
        public PluginDescriptionAttribute(string description)
        {
            Description = description;
        }

        public string Description { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class PluginAuthorAttribute : Attribute
    {
        public PluginAuthorAttribute(string author)
        {
            Author = author;
        }

        public string Author { get; }
    }

    public interface IPlugin
    {
        void Init(PluginManager pluginManager);

        void End(PluginManager pluginManager);
    }

    public interface IDataPlugin
    {
        void DataUpdate(PluginManager pluginManager, ref GameData data);
    }

    public interface IWPFSettingsV2
    {
        string LeftMenuTitle { get; }

        ImageSource PictureIcon { get; }
    }

    public interface IWPFSettings
    {
        WpfControl GetWPFSettingsControl(PluginManager pluginManager);
    }

    public class PluginManager
    {
        public virtual string GetCommonStoragePath(params string[] pathParts)
        {
            return pathParts == null ? string.Empty : Path.Combine(pathParts);
        }

        public virtual string GetCommonStoragePath(bool create, params string[] pathParts)
        {
            return GetCommonStoragePath(pathParts);
        }

        public virtual void AddProperty<T>(string propertyName, Type ownerType, T initialValue, string unit = null)
        {
        }

        public virtual void SetPropertyValue(string propertyName, Type ownerType, object value)
        {
        }
    }
}

namespace SimHub.Plugins.Styles
{
    public class SHTabControl : WpfTabControl
    {
    }

    public class SHTabItem : WpfTabItem
    {
    }

    public class SHSection : WpfGroupBox
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(SHSection),
                new PropertyMetadata(string.Empty, OnTitleChanged));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        private static void OnTitleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        {
            if (dependencyObject is SHSection section)
            {
                section.Header = args.NewValue;
            }
        }
    }
}

namespace SimHub.Logging
{
    public interface ILogger
    {
        void Info(string message);

        void Warn(string message);

        void Error(string message);
    }

    public sealed class NullLogger : ILogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message)
        {
        }
    }

    public static class Current
    {
        private static ILogger _logger = new NullLogger();

        public static ILogger Logger
        {
            get => _logger;
            set => _logger = value ?? new NullLogger();
        }

        public static void Info(string message)
        {
            _logger.Info(message);
        }

        public static void Warn(string message)
        {
            _logger.Warn(message);
        }

        public static void Error(string message)
        {
            _logger.Error(message);
        }
    }
}
