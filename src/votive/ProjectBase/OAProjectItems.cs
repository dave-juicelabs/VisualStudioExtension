// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

using System;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Reflection;
using IServiceProvider = System.IServiceProvider;
using Microsoft.VisualStudio.OLE.Interop;
using EnvDTE;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;


namespace Microsoft.VisualStudio.Package.Automation
{
	/// <summary>
	/// Contains ProjectItem objects
	/// </summary>
	[SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
	[ComVisible(true), CLSCompliant(false)]
	public class OAProjectItems : OANavigableProjectItems
	{
		#region ctor
		public OAProjectItems(OAProject project, HierarchyNode nodeWithItems)
			: base(project, nodeWithItems)
		{
		}
		#endregion

		#region EnvDTE.ProjectItems
		/// <summary>
		/// Creates a new project item from an existing item template file and adds it to the project. 
		/// </summary>
		/// <param name="fileName">The full path and file name of the template project file.</param>
		/// <param name="name">The file name to use for the new project item.</param>
		/// <returns>A ProjectItem object. </returns>
		public override EnvDTE.ProjectItem AddFromTemplate(string fileName, string name)
		{

			if (this.Project == null || this.Project.Project == null || this.Project.Project.Site == null || this.Project.Project.IsClosed)
			{
				throw new InvalidOperationException();
			}

			ProjectNode proj = this.Project.Project;

			IVsExtensibility3 extensibility = this.Project.Project.Site.GetService(typeof(IVsExtensibility)) as IVsExtensibility3;

			if (extensibility == null)
			{
				throw new InvalidOperationException();
			}

			EnvDTE.ProjectItem itemAdded = null;
			extensibility.EnterAutomationFunction();

			try
			{
				// Determine the operation based on the extension of the filename.
				// We should run the wizard only if the extension is vstemplate
				// otherwise it's a clone operation
				VSADDITEMOPERATION op;

				if (Utilities.IsTemplateFile(fileName))
				{
					op = VSADDITEMOPERATION.VSADDITEMOP_RUNWIZARD;
				}
				else
				{
					op = VSADDITEMOPERATION.VSADDITEMOP_CLONEFILE;
				}

				VSADDRESULT[] result = new VSADDRESULT[1];

				// It is not a very good idea to throw since the AddItem might return Cancel or Abort.
				// The problem is that up in the call stack the wizard code does not check whether it has received a ProjectItem or not and will crash.
				// The other problem is that we cannot get add wizard dialog back if a cancel or abort was returned because we throw and that code will never be executed. Typical catch 22.
				ErrorHandler.ThrowOnFailure(proj.AddItem(this.NodeWithItems.ID, op, name, 0, new string[1] { fileName }, IntPtr.Zero, result));

				string fileDirectory = proj.GetBaseDirectoryForAddingFiles(this.NodeWithItems);
				string templateFilePath = System.IO.Path.Combine(fileDirectory, name);
				itemAdded = this.EvaluateAddResult(result[0], templateFilePath);
			}
			finally
			{
				extensibility.ExitAutomationFunction();
			}

			return itemAdded;
		}

		/// <summary>
		/// Adds a folder to the collection of ProjectItems with the given name.
		/// 
		/// The kind must be null, empty string, or the string value of vsProjectItemKindPhysicalFolder.
		/// Virtual folders are not supported by this implementation.
		/// </summary>
		/// <param name="name">The name of the new folder to add</param>
		/// <param name="kind">A string representing a Guid of the folder kind.</param>
		/// <returns>A ProjectItem representing the newly added folder.</returns>
		public override ProjectItem AddFolder(string name, string kind)
		{
			if (this.Project == null || this.Project.Project == null || this.Project.Project.Site == null || this.Project.Project.IsClosed)
			{
				throw new InvalidOperationException();
			}
			//Verify name is not null or empty
			Utilities.ValidateFileName(this.Project.Project.Site, name);

			//Verify that kind is null, empty, or a physical folder
			if (!(String.IsNullOrEmpty(kind) || kind.Equals(EnvDTE.Constants.vsProjectItemKindPhysicalFolder)))
			{
				throw new ArgumentException("Parameter specification for AddFolder was not meet", "kind");
			}

			for (HierarchyNode child = this.NodeWithItems.FirstChild; child != null; child = child.NextSibling)
			{
				if (child.Caption.Equals(name, StringComparison.OrdinalIgnoreCase))
				{
					throw new ArgumentException(String.Format(CultureInfo.CurrentCulture, "Folder already exists with the name '{0}'", name));
				}
			}

			ProjectNode proj = this.Project.Project;

			IVsExtensibility3 extensibility = this.Project.Project.Site.GetService(typeof(IVsExtensibility)) as IVsExtensibility3;

			if (extensibility == null)
			{
				throw new InvalidOperationException();
			}

			HierarchyNode newFolder = null;
			extensibility.EnterAutomationFunction();

			//In the case that we are adding a folder to a folder, we need to build up
			//the path to the project node.
			name = Path.Combine(this.NodeWithItems.VirtualNodeName, name);

			try
			{
				newFolder = proj.CreateFolderNodes(name);
			}
			finally
			{
				extensibility.ExitAutomationFunction();
			}

			return newFolder.GetAutomationObject() as ProjectItem;
		}

		/// <summary>
		/// Copies a source file and adds it to the project.
		/// </summary>
		/// <param name="filePath">The path and file name of the project item to be added.</param>
		/// <returns>A ProjectItem object. </returns>
		public override EnvDTE.ProjectItem AddFromFileCopy(string filePath)
		{
			return this.AddItem(filePath, VSADDITEMOPERATION.VSADDITEMOP_OPENFILE);
		}

		/// <summary>
		/// Adds a project item from a file that is installed in a project directory structure. 
		/// </summary>
		/// <param name="fileName">The file name of the item to add as a project item. </param>
		/// <returns>A ProjectItem object. </returns>
		public override EnvDTE.ProjectItem AddFromFile(string fileName)
		{
			return this.AddItem(fileName, VSADDITEMOPERATION.VSADDITEMOP_OPENFILE);
		}

		/// <summary>
		/// Adds a project item which is a link to a file outside the project directory structure.
		/// </summary>
		/// <param name="fileName">The file to be linked to the project.</param>
		/// <returns>A ProjectItem object.</returns>
		public override EnvDTE.ProjectItem AddFileLink(string fileName)
		{
			return this.AddItem(fileName, VSADDITEMOPERATION.VSADDITEMOP_LINKTOFILE);
		}
		#endregion

		#region helper methods
		/// <summary>
		/// Adds an item to the project.
		/// </summary>
		/// <param name="path">The full path of the item to add.</param>
		/// <param name="op">The <paramref name="VSADDITEMOPERATION"/> to use when adding the item.</param>
		/// <returns>A ProjectItem object. </returns>
		protected virtual EnvDTE.ProjectItem AddItem(string path, VSADDITEMOPERATION op)
		{
			if (this.Project == null || this.Project.Project == null || this.Project.Project.Site == null || this.Project.Project.IsClosed)
			{
				throw new InvalidOperationException();
			}

			ProjectNode proj = this.Project.Project;

			IVsExtensibility3 extensibility = this.Project.Project.Site.GetService(typeof(IVsExtensibility)) as IVsExtensibility3;

			if (extensibility == null)
			{
				throw new InvalidOperationException();
			}

			EnvDTE.ProjectItem itemAdded = null;
			extensibility.EnterAutomationFunction();

			try
			{
				VSADDRESULT[] result = new VSADDRESULT[1];
				ErrorHandler.ThrowOnFailure(proj.AddItem(this.NodeWithItems.ID, op, path, 0, new string[1] { path }, IntPtr.Zero, result));

				string fileName = System.IO.Path.GetFileName(path);
				string fileDirectory = proj.GetBaseDirectoryForAddingFiles(this.NodeWithItems);
				string filePathInProject = System.IO.Path.Combine(fileDirectory, fileName);

				itemAdded = this.EvaluateAddResult(result[0], filePathInProject);
			}
			finally
			{
				extensibility.ExitAutomationFunction();
			}

			return itemAdded;
		}

		/// <summary>
		/// Evaluates the result of an add operation.
		/// </summary>
		/// <param name="result">The <paramref name="VSADDRESULT"/> returned by the Add methods</param>
		/// <param name="path">The full path of the item added.</param>
		/// <returns>A ProjectItem object.</returns>
		protected virtual EnvDTE.ProjectItem EvaluateAddResult(VSADDRESULT result, string path)
		{
			if (result == VSADDRESULT.ADDRESULT_Success)
			{
				HierarchyNode nodeAdded = this.NodeWithItems.FindChild(path);
				Debug.Assert(nodeAdded != null, "We should have been able to find the new element in the hierarchy");
				if (nodeAdded != null)
				{
					EnvDTE.ProjectItem item = null;
					if (nodeAdded is FileNode)
					{
						item = new OAFileItem(this.Project, nodeAdded as FileNode);
					}
					else if (nodeAdded is NestedProjectNode)
					{
						item = new OANestedProjectItem(this.Project, nodeAdded as NestedProjectNode);
					}
					else
					{
						item = new OAProjectItem<HierarchyNode>(this.Project, nodeAdded);
					}

					this.Items.Add(item);
					return item;
				}
			}
			return null;
		}
		#endregion
	}
}
