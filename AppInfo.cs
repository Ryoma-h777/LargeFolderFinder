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
                var title = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyTitleAttribute>()?
                    .Title;
                return !string.IsNullOrEmpty(title) ? title : "Large Folder Finder";
            }
        }

        public static string Version =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "1.0.0";

        public static string Copyright =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyCopyrightAttribute>()?
                .Copyright ?? "Copyright (C) 2026";
    }
}
