﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Taskmaster.Properties {
    using System;


    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "15.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources {

        static global::System.Resources.ResourceManager resourceMan;

        static global::System.Globalization.CultureInfo resourceCulture;

        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }

        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Taskmaster.Properties.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }

        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to 2019/01/12 09:54:19 Z
        ///.
        /// </summary>
        internal static string BuildDate {
            get {
                return ResourceManager.GetString("BuildDate", resourceCulture);
            }
        }

        internal static string KnownModules {
            get {
                return ResourceManager.GetString("KnownModules", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to The MIT License (MIT)
        ///
        ///Author:
        ///		M.A. (https://github.com/mkahvi)
        ///
        ///Copyright (c) 2016-2018 M.A.
        ///
        ///Permission is hereby granted, free of charge, to any person obtaining a copy
        ///of this software and associated documentation files (the &quot;Software&quot;), to deal
        ///in the Software without restriction, including without limitation the rights
        ///to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        ///copies of the Software, and to permit persons to whom the Software is
        ///furnished to do so, subject [rest of string was truncated]&quot;;.
        /// </summary>
        internal static string LICENSE {
            get {
                return ResourceManager.GetString("LICENSE", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to C:\Users\Mayflower\Documents\Projects\TaskMaster\Taskmaster\
        ///.
        /// </summary>
        internal static string ProjectDirectory {
            get {
                return ResourceManager.GetString("ProjectDirectory", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to # Default Configuration
        ///
        ///#[Human-readable Unique Name]
        ///#Image=executable.exe
        ///#Priority=2 # Process priority, 0 [low] to 4 [high], 2 = normal/Default
        ///#Priority strategy = 2 # 0 = Ignore/Unset, 1 = Increase only, 2 = Decrease only, 3 = Force/bidirectional
        ///#Rescan=30 # After how many minutes should the process be checked again
        ///#Allow paging=false # Allow TM to push the process into swap file
        ///
        ///[Internet Explorer]
        ///Image=iexlore.exe
        ///Priority=1
        ///Priority strategy = 2
        ///#Rescan=30
        ///Allow paging=false
        ///
        ///[Google Chrome]
        ///I [rest of string was truncated]&quot;;.
        /// </summary>
        internal static string Watchlist {
            get {
                return ResourceManager.GetString("Watchlist", resourceCulture);
            }
        }
    }
}
