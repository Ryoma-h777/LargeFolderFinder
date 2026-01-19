using System.Reflection;

namespace LargeFolderFinder
{
    public static class AppInfo
    {
        public const string Organization = "Cat & Chocolate Laboratory";

        public static string Title
        {
            get
            {
                var titleAttr = (AssemblyTitleAttribute?)System.Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyTitleAttribute));
                return titleAttr?.Title ?? "Large Folder Finder";
            }
        }

        public static string Version
        {
            get
            {
                var versionAttr = (AssemblyInformationalVersionAttribute?)System.Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
                return versionAttr?.InformationalVersion ?? "1.0.0";
            }
        }

        public static string Copyright
        {
            get
            {
                var copyrightAttr = (AssemblyCopyrightAttribute?)System.Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyCopyrightAttribute));
                return copyrightAttr?.Copyright ?? "Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory";
            }
        }
    }
}
