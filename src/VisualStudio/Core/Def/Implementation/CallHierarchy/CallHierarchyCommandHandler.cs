﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.CodeAnalysis.SymbolMapping;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.CallHierarchy
{
    [Export(typeof(VisualStudio.Commanding.ICommandHandler))]
    [ContentType(ContentTypeNames.CSharpContentType)]
    [ContentType(ContentTypeNames.VisualBasicContentType)]
    [Name("CallHierarchy")]
    [Order(After = PredefinedCommandHandlerNames.DocumentationComments)]
    internal class CallHierarchyCommandHandler : VisualStudio.Commanding.ICommandHandler<ViewCallHierarchyCommandArgs>
    {
        private readonly ICallHierarchyPresenter _presenter;
        private readonly CallHierarchyProvider _provider;

        public string DisplayName => EditorFeaturesResources.View_Call_Hierarchy_Command_Handler_Display_Name;

        [ImportingConstructor]
        public CallHierarchyCommandHandler([ImportMany] IEnumerable<ICallHierarchyPresenter> presenters, CallHierarchyProvider provider)
        {
            _presenter = presenters.FirstOrDefault();
            _provider = provider;
        }

        public bool ExecuteCommand(ViewCallHierarchyCommandArgs args, CommandExecutionContext context)
        {
            AddRootNode(args, context);
            return true;
        }

        private void AddRootNode(ViewCallHierarchyCommandArgs args, CommandExecutionContext context)
        {
            context.WaitContext.AllowCancellation = true;
            context.WaitContext.Description = EditorFeaturesResources.Computing_Call_Hierarchy_Information;

            var cancellationToken = context.WaitContext.CancellationToken;
            var document = args.SubjectBuffer.CurrentSnapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document == null)
            {
                return;
            }

            var workspace = document.Project.Solution.Workspace;
            var semanticModel = document.GetSemanticModelAsync(cancellationToken).WaitAndGetResult(cancellationToken);

            var caretPosition = args.TextView.Caret.Position.BufferPosition.Position;
            var symbolUnderCaret = SymbolFinder.FindSymbolAtPositionAsync(semanticModel, caretPosition, workspace, cancellationToken)
                .WaitAndGetResult(cancellationToken);

            if (symbolUnderCaret != null)
            {
                // Map symbols so that Call Hierarchy works from metadata-as-source
                var mappingService = document.Project.Solution.Workspace.Services.GetService<ISymbolMappingService>();
                var mapping = mappingService.MapSymbolAsync(document, symbolUnderCaret, cancellationToken).WaitAndGetResult(cancellationToken);

                if (mapping.Symbol != null)
                {
                    var node = _provider.CreateItem(mapping.Symbol, mapping.Project, SpecializedCollections.EmptyEnumerable<Location>(), cancellationToken).WaitAndGetResult(cancellationToken);
                    if (node != null)
                    {
                        _presenter.PresentRoot((CallHierarchyItem)node);
                    }
                }
            }
            else
            {
                var notificationService = document.Project.Solution.Workspace.Services.GetService<INotificationService>();
                notificationService.SendNotification(EditorFeaturesResources.Cursor_must_be_on_a_member_name, severity: NotificationSeverity.Information);
            }
        }

        public VisualStudio.Commanding.CommandState GetCommandState(ViewCallHierarchyCommandArgs args)
        {
            return VisualStudio.Commanding.CommandState.CommandIsAvailable;
        }
    }
}
