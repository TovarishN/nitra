﻿//------------------------------------------------------------------------------
// <copyright file="VSPackage.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using System.ComponentModel;
using Nitra.ClientServer.Client;
using Nitra.VisualStudio;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell.Events;

namespace Nitra.VisualStudio
{
  /// <summary>
  /// This is the class that implements the package exposed by this assembly.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The minimum requirement for a class to be considered a valid package for Visual Studio
  /// is to implement the IVsPackage interface and register itself with the shell.
  /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
  /// to do it: it derives from the Package class that provides the implementation of the
  /// IVsPackage interface and uses the registration attributes defined in the framework to
  /// register itself and its components with the shell. These attributes tell the pkgdef creation
  /// utility what data to put into .pkgdef file.
  /// </para>
  /// <para>
  /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
  /// </para>
  /// </remarks>
  [ProvideAutoLoad(UIContextGuids80.NoSolution)]
  [Description("Nitra Package.")]
  [PackageRegistration(UseManagedResourcesOnly = true)]
  [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
  [Guid(VSPackage.PackageGuidString)]
  [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
  public sealed class VSPackage : Package
  {
    /// <summary>VSPackage GUID string.</summary>
    public const string PackageGuidString = "66c3f4cd-1547-458b-a321-83f0c448b4d3";

    public static VSPackage Instance;

    public NitraClient Client { get; private set; }

    private RunningDocTableEvents                       _runningDocTableEventse;
    private Dictionary<IVsHierarchy, HierarchyListener> _listenersMap = new Dictionary<IVsHierarchy, HierarchyListener>();
    private string                                      _loadingProjectPath;
    private Guid                                        _loadingProject;

    /// <summary>
    /// Initializes a new instance of the <see cref="VSPackage"/> class.
    /// </summary>
    public VSPackage()
    {
      // Inside this method you can place any initialization code that does not require
      // any Visual Studio service because at this point the package object is created but
      // not sited yet inside Visual Studio environment. The place to do all the other
      // initialization is the Initialize method.
    }

    #region Package Members

    /// <summary>
    /// Initialization of the package; this method is called right after the package is sited, so this is the place
    /// where you can put all the initialization code that rely on services provided by VisualStudio.
    /// </summary>
    protected override void Initialize()
    {
      base.Initialize();
      Debug.Assert(Instance == null);
      Instance = this;
      var stringManager = new StringManager();
      Client = new NitraClient(stringManager);
      Debug.WriteLine("tr: NitraClient created.");

      SubscibeToSolutionEvents();
      _runningDocTableEventse = new RunningDocTableEvents();
    }

    protected override void Dispose(bool disposing)
    {
      try
      {
        Client?.Dispose();
        UnsubscibeToSolutionEvents();
        _runningDocTableEventse?.Dispose();
      }
      finally
      {
        base.Dispose(disposing);
      }
    }

    #endregion

    public void SetConfig(string projectSupport)
    {
    }

    private void SolutionEvents_OnQueryUnloadProject(object sender, CancelHierarchyEventArgs e)
    {
      var hierarchy = e.Hierarchy;
      var project = hierarchy.GetProp<EnvDTE.Project>(VSConstants.VSITEMID_ROOT, __VSHPROPID.VSHPROPID_ExtObject);
      Debug.WriteLine($"tr: QueryUnloadProject(FullName='{project.FullName}')");
    }

    private void SolutionEvents_OnQueryCloseSolution(object sender, CancelEventArgs e)
    {
      Debug.WriteLine($"tr: QueryCloseSolution(Cancel='{e.Cancel}')");
    }

    private void SolutionEvents_OnQueryCloseProject(object sender, QueryCloseProjectEventArgs e)
    {
      var hierarchy = e.Hierarchy;
      var project = hierarchy.GetProp<EnvDTE.Project>(VSConstants.VSITEMID_ROOT, __VSHPROPID.VSHPROPID_ExtObject);
      Debug.WriteLine($"tr: QueryCloseProject(IsRemoving='{e.IsRemoving}', Cancel='{e.Cancel}', FullName='{project.FullName}')");
    }

    private void SolutionEvents_OnQueryChangeProjectParent(object sender, QueryChangeProjectParentEventArgs e)
    {
      Debug.WriteLine($"tr: QueryChangeProjectParent(Hierarchy='{e.Hierarchy}', NewParentHierarchy='{e.NewParentHierarchy}', Cancel='{e.Cancel}')");
    }

    private void SolutionEvents_OnQueryBackgroundLoadProjectBatch(object sender, QueryLoadProjectBatchEventArgs e)
    {
      Debug.WriteLine($"tr: QueryBackgroundLoadProjectBatch(ShouldDelayLoadToNextIdle='{e.ShouldDelayLoadToNextIdle}')");
    }

    private void SolutionEvents_OnBeforeUnloadProject(object sender, LoadProjectEventArgs e)
    {
      Debug.WriteLine($"tr: BeforeUnloadProject(RealHierarchy='{e.RealHierarchy}', StubHierarchy='{e.StubHierarchy}')");
    }

    private void SolutionEvents_OnBeforeOpenSolution(object sender, BeforeOpenSolutionEventArgs e)
    {
      Debug.WriteLine($"tr: BeforeOpenSolution(SolutionFilename='{e.SolutionFilename}')");
    }

    private void SolutionEvents_OnBeforeOpenProject(object sender, BeforeOpenProjectEventArgs e)
    {
      _loadingProjectPath = e.Filename;
      _loadingProject = e.Project;
      Debug.WriteLine($"tr: BeforeOpenProject(Filename='{e.Filename}', Project='{e.Project}'  ProjectType='{e.ProjectType}')");
    }

    private void SolutionEvents_OnBeforeOpeningChildren(object sender, HierarchyEventArgs e)
    {
      Debug.WriteLine($"tr: BeforeOpeningChildren(Hierarchy='{e.Hierarchy}')");
    }

    private void SolutionEvents_OnBeforeLoadProjectBatch(object sender, LoadProjectBatchEventArgs e)
    {
      Debug.WriteLine($"tr: BeforeLoadProjectBatch(IsBackgroundIdleBatch='{e.IsBackgroundIdleBatch}')");
    }

    private void SolutionEvents_OnBeforeClosingChildren(object sender, HierarchyEventArgs e)
    {
      Debug.WriteLine($"tr: BeforeClosingChildren(Hierarchy='{e.Hierarchy}')");
    }

    private void SolutionEvents_OnBeforeCloseSolution(object sender, EventArgs e)
    {
      Debug.WriteLine($"tr: BeforeCloseSolution()");
    }

    private void SolutionEvents_OnBeforeBackgroundSolutionLoadBegins(object sender, EventArgs e)
    {
      Debug.WriteLine($"tr: BeforeBackgroundSolutionLoadBegins()");
    }

    private void SolutionEvents_OnAfterRenameProject(object sender, HierarchyEventArgs e)
    {
      var hierarchy = e.Hierarchy;
      var project = hierarchy.GetProp<EnvDTE.Project>(VSConstants.VSITEMID_ROOT, __VSHPROPID.VSHPROPID_ExtObject);
      Debug.WriteLine($"tr: AfterRenameProject(Hierarchy='{hierarchy}', FullName='{project.FullName}')");
    }

    private void SolutionEvents_OnAfterOpenSolution(object sender, OpenSolutionEventArgs e)
    {
      Debug.WriteLine($"tr: AfterOpenSolution(IsNewSolution='{e.IsNewSolution}')");
    }

    private void SolutionEvents_OnAfterOpenProject(object sender, OpenProjectEventArgs e)
    {
      var hierarchy = e.Hierarchy;
      var project = hierarchy.GetProp<EnvDTE.Project>(VSConstants.VSITEMID_ROOT, __VSHPROPID.VSHPROPID_ExtObject);

      Debug.WriteLine($"tr: AfterOpenProject(IsAdded='{e.IsAdded}', FullName='{project.FullName}')");

      var listener = new HierarchyListener(hierarchy);

      listener.ItemAdded += FileAdded;
      listener.ItemDeleted += FileDeleted;
      listener.ReferenceAdded += Listener_ReferenceAdded;
      listener.StartListening(true);

      _listenersMap.Add(hierarchy, listener);

      // We need apdate all references when a project adding in exist solution
      if (e.IsAdded)
      {
      }
    }

    private void Listener_ReferenceAdded(object sender, ReferenceEventArgs e)
    {
      Debug.WriteLine($"tr: ReferenceAdded(FileName='{e.Reference.Path}')");
    }

    private void SolutionEvents_OnBeforeCloseProject(object sender, CloseProjectEventArgs e)
    {
      var hierarchy = e.Hierarchy;
      var project = hierarchy.GetProp<EnvDTE.Project>(VSConstants.VSITEMID_ROOT, __VSHPROPID.VSHPROPID_ExtObject);

      Debug.WriteLine($"tr: BeforeCloseProject(IsRemoved='{e.IsRemoved}', FullName='{project.FullName}')");

      var listener = _listenersMap[hierarchy];
      listener.StopListening();
      listener.Dispose();
      _listenersMap.Remove(hierarchy);
    }

    private void FileAdded(object sender, HierarchyItemEventArgs e)
    {
      var fileName = e.FileName;

      string action = e.Hierarchy.GetProp<string>(e.ItemId, __VSHPROPID4.VSHPROPID_BuildAction);

      Debug.WriteLine($"tr: FileAdded(BuildAction='{action}', FileName='{fileName}')");
    }

    private void FileDeleted(object sender, HierarchyItemEventArgs e)
    {
      Debug.WriteLine($"tr: FileAdded(FileName='{e.FileName}')");
    }

    private void SolutionEvents_OnAfterOpeningChildren(object sender, Microsoft.VisualStudio.Shell.Events.HierarchyEventArgs e)
    {
      Debug.WriteLine($"tr: AfterOpeningChildren(Hierarchy='{e.Hierarchy}')");
    }

    private void SolutionEvents_OnAfterMergeSolution(object sender, EventArgs e)
    {
      Debug.WriteLine($"tr: AfterMergeSolution()");
    }

    private void SolutionEvents_OnAfterLoadProjectBatch(object sender, LoadProjectBatchEventArgs e)
    {
      Debug.WriteLine($"tr: AfterLoadProjectBatch(IsBackgroundIdleBatch='{e.IsBackgroundIdleBatch}')");
    }

    private void SolutionEvents_OnAfterLoadProject(object sender, LoadProjectEventArgs e)
    {
      Debug.WriteLine($"tr: AfterLoadProject(RealHierarchy='{e.RealHierarchy}', StubHierarchy='{e.StubHierarchy}')");
    }

    private void SolutionEvents_OnAfterClosingChildren(object sender, Microsoft.VisualStudio.Shell.Events.HierarchyEventArgs e)
    {
      Debug.WriteLine($"tr: AfterClosingChildren(Hierarchy='{e.Hierarchy}')");
    }

    private void SolutionEvents_OnAfterCloseSolution(object sender, EventArgs e)
    {
      Debug.WriteLine("tr: AfterCloseSolution()");
    }

    private void SolutionEvents_OnAfterChangeProjectParent(object sender, Microsoft.VisualStudio.Shell.Events.HierarchyEventArgs e)
    {
      Debug.WriteLine($"tr: AfterChangeProjectParent(Hierarchy='{e.Hierarchy}')");
    }

    private void SolutionEvents_OnAfterBackgroundSolutionLoadComplete(object sender, EventArgs e)
    {
      Debug.WriteLine("tr: AfterBackgroundSolutionLoadComplete()");
    }

    private void SolutionEvents_OnAfterAsynchOpenProject(object sender, OpenProjectEventArgs e)
    {
      Debug.WriteLine($"tr: AfterChangeProjectParent(Hierarchy='{e.Hierarchy}', IsAdded='{e.IsAdded}')");
    }



    private void SubscibeToSolutionEvents()
    {
      SolutionEvents.OnAfterAsynchOpenProject += SolutionEvents_OnAfterAsynchOpenProject;
      SolutionEvents.OnAfterBackgroundSolutionLoadComplete += SolutionEvents_OnAfterBackgroundSolutionLoadComplete;
      SolutionEvents.OnAfterChangeProjectParent += SolutionEvents_OnAfterChangeProjectParent;
      SolutionEvents.OnAfterCloseSolution += SolutionEvents_OnAfterCloseSolution;
      SolutionEvents.OnAfterClosingChildren += SolutionEvents_OnAfterClosingChildren;
      SolutionEvents.OnAfterLoadProject += SolutionEvents_OnAfterLoadProject;
      SolutionEvents.OnAfterLoadProjectBatch += SolutionEvents_OnAfterLoadProjectBatch;
      SolutionEvents.OnAfterMergeSolution += SolutionEvents_OnAfterMergeSolution;
      SolutionEvents.OnAfterOpeningChildren += SolutionEvents_OnAfterOpeningChildren;
      SolutionEvents.OnAfterOpenProject += SolutionEvents_OnAfterOpenProject;
      SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;
      SolutionEvents.OnAfterRenameProject += SolutionEvents_OnAfterRenameProject;
      SolutionEvents.OnBeforeBackgroundSolutionLoadBegins += SolutionEvents_OnBeforeBackgroundSolutionLoadBegins;
      SolutionEvents.OnBeforeCloseProject += SolutionEvents_OnBeforeCloseProject;
      SolutionEvents.OnBeforeCloseSolution += SolutionEvents_OnBeforeCloseSolution;
      SolutionEvents.OnBeforeClosingChildren += SolutionEvents_OnBeforeClosingChildren;
      SolutionEvents.OnBeforeLoadProjectBatch += SolutionEvents_OnBeforeLoadProjectBatch;
      SolutionEvents.OnBeforeOpeningChildren += SolutionEvents_OnBeforeOpeningChildren;
      SolutionEvents.OnBeforeOpenProject += SolutionEvents_OnBeforeOpenProject;
      SolutionEvents.OnBeforeOpenSolution += SolutionEvents_OnBeforeOpenSolution;
      SolutionEvents.OnBeforeUnloadProject += SolutionEvents_OnBeforeUnloadProject;
      SolutionEvents.OnQueryBackgroundLoadProjectBatch += SolutionEvents_OnQueryBackgroundLoadProjectBatch;
      SolutionEvents.OnQueryChangeProjectParent += SolutionEvents_OnQueryChangeProjectParent;
      SolutionEvents.OnQueryCloseProject += SolutionEvents_OnQueryCloseProject;
      SolutionEvents.OnQueryCloseSolution += SolutionEvents_OnQueryCloseSolution;
      SolutionEvents.OnQueryUnloadProject += SolutionEvents_OnQueryUnloadProject;
    }

    private void UnsubscibeToSolutionEvents()
    {
      SolutionEvents.OnAfterAsynchOpenProject -= SolutionEvents_OnAfterAsynchOpenProject;
      SolutionEvents.OnAfterBackgroundSolutionLoadComplete -= SolutionEvents_OnAfterBackgroundSolutionLoadComplete;
      SolutionEvents.OnAfterChangeProjectParent -= SolutionEvents_OnAfterChangeProjectParent;
      SolutionEvents.OnAfterCloseSolution -= SolutionEvents_OnAfterCloseSolution;
      SolutionEvents.OnAfterClosingChildren -= SolutionEvents_OnAfterClosingChildren;
      SolutionEvents.OnAfterLoadProject -= SolutionEvents_OnAfterLoadProject;
      SolutionEvents.OnAfterLoadProjectBatch -= SolutionEvents_OnAfterLoadProjectBatch;
      SolutionEvents.OnAfterMergeSolution -= SolutionEvents_OnAfterMergeSolution;
      SolutionEvents.OnAfterOpeningChildren -= SolutionEvents_OnAfterOpeningChildren;
      SolutionEvents.OnAfterOpenProject -= SolutionEvents_OnAfterOpenProject;
      SolutionEvents.OnAfterOpenSolution -= SolutionEvents_OnAfterOpenSolution;
      SolutionEvents.OnAfterRenameProject -= SolutionEvents_OnAfterRenameProject;
      SolutionEvents.OnBeforeBackgroundSolutionLoadBegins -= SolutionEvents_OnBeforeBackgroundSolutionLoadBegins;
      SolutionEvents.OnBeforeCloseProject -= SolutionEvents_OnBeforeCloseProject;
      SolutionEvents.OnBeforeCloseSolution -= SolutionEvents_OnBeforeCloseSolution;
      SolutionEvents.OnBeforeClosingChildren -= SolutionEvents_OnBeforeClosingChildren;
      SolutionEvents.OnBeforeLoadProjectBatch -= SolutionEvents_OnBeforeLoadProjectBatch;
      SolutionEvents.OnBeforeOpeningChildren -= SolutionEvents_OnBeforeOpeningChildren;
      SolutionEvents.OnBeforeOpenProject -= SolutionEvents_OnBeforeOpenProject;
      SolutionEvents.OnBeforeOpenSolution -= SolutionEvents_OnBeforeOpenSolution;
      SolutionEvents.OnBeforeUnloadProject -= SolutionEvents_OnBeforeUnloadProject;
      SolutionEvents.OnQueryBackgroundLoadProjectBatch -= SolutionEvents_OnQueryBackgroundLoadProjectBatch;
      SolutionEvents.OnQueryChangeProjectParent -= SolutionEvents_OnQueryChangeProjectParent;
      SolutionEvents.OnQueryCloseProject -= SolutionEvents_OnQueryCloseProject;
      SolutionEvents.OnQueryCloseSolution -= SolutionEvents_OnQueryCloseSolution;
      SolutionEvents.OnQueryUnloadProject -= SolutionEvents_OnQueryUnloadProject;
    }
  }
}
