﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using DocumentDbExplorer.Infrastructure;
using DocumentDbExplorer.Infrastructure.Extensions;
using DocumentDbExplorer.Infrastructure.Models;
using DocumentDbExplorer.Properties;
using DocumentDbExplorer.Services;
using DocumentDbExplorer.Services.DialogSettings;
using GalaSoft.MvvmLight.Ioc;
using GalaSoft.MvvmLight.Messaging;
using GalaSoft.MvvmLight.Threading;
using Microsoft.Azure.Documents;

namespace DocumentDbExplorer.ViewModel
{
    public class DocumentsTabViewModel : PaneViewModel, ICanZoom, IHaveQuerySettings
    {
        private readonly IDocumentDbService _dbService;
        private readonly IDialogService _dialogService;
        private DocumentDescription _selectedDocument;
        private RelayCommand _loadMoreCommand;
        private RelayCommand _refreshLoadCommand;
        private RelayCommand _newDocumentCommand;
        private RelayCommand _discardCommand;
        private RelayCommand _saveDocumentCommand;
        private RelayCommand _deleteDocumentCommand;
        private RelayCommand _editFilterCommand;
        private RelayCommand _applyFilterCommand;
        private RelayCommand _closeFilterCommand;
        private RelayCommand _saveLocalCommand;
        private DocumentNodeViewModel _node;
        private Document _currentDocument;

        public DocumentsTabViewModel(IMessenger messenger, IDocumentDbService dbService, IDialogService dialogService) : base(messenger)
        {
            Documents = new ObservableCollection<DocumentDescription>();
            _dbService = dbService;
            _dialogService = dialogService;
            EditorViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<DocumentEditorViewModel>();
            Title = "Documents";
            Header = Title;
            //IconSource = new Uri(@"/DocumentDbExplorer;component/Images/Paste.png", UriKind.RelativeOrAbsolute);
        }

        public DocumentNodeViewModel Node
        {
            get { return _node; }
            set
            {
                if (_node != value)
                {
                    _node = value;
                   
                    var split = Node.Parent.Collection.AltLink.Split(new char[] { '/' });
                    ToolTip = $"{split[1]}>{split[3]}>{Title}";
                }
            }
        }

        public ObservableCollection<DocumentDescription> Documents { get; }

        public DocumentDescription SelectedDocument { get; set; }

        public async void OnSelectedDocumentChanged()
        {
            if (SelectedDocument != null)
            {
                if (_currentDocument?.Id != SelectedDocument.Id)
                {
                    _currentDocument = await _dbService.GetDocument(Node.Parent.Parent.Parent.Connection, SelectedDocument);
                }

                EditorViewModel.SetText(_currentDocument, HideSystemProperties);
            }
            else
            {
                EditorViewModel.SetText(null, HideSystemProperties);
            }
        }

        public string Filter { get; set; }

        public bool IsEditingFilter { get; set; }

        public bool HasMore { get; set; }
        public string ContinuationToken { get; set; }

        public async Task LoadDocuments()
        {
            try
            {
                var result = await _dbService.GetDocuments(Node.Parent.Parent.Parent.Connection,
                                       Node.Parent.Collection,
                                       Filter,
                                       Settings.Default.MaxDocumentToRetrieve,
                                       ContinuationToken);

                var list = result as DocumentDescriptionList;
                HasMore = list.HasMore;
                ContinuationToken = list.ContinuationToken;

                foreach (var document in list)
                {
                    Documents.Add(document);
                }

                TotalItemsCount = list.CollectionSize;
                RaisePropertyChanged(() => ItemsCount);
            }
            catch (DocumentClientException clientEx)
            {
                var errors = clientEx.Parse();
                await _dialogService.ShowError(errors.ToString(), "Error", "ok", null);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowError(ex, "Error", "ok", null);
            }
        }

        public long TotalItemsCount { get; set; }
        public long ItemsCount => Documents.Count;
        
        public DocumentEditorViewModel EditorViewModel { get; set; }

        protected Connection Connection => Node.Parent.Parent.Parent.Connection;

        protected DocumentCollection Collection => Node.Parent.Collection;

        public RelayCommand LoadMoreCommand
        {
            get
            {
                return _loadMoreCommand
                    ?? (_loadMoreCommand = new RelayCommand(
                        async x => await LoadDocuments()));
            }
        }

        public RelayCommand RefreshLoadCommand
        {
            get
            {
                return _refreshLoadCommand
                    ?? (_refreshLoadCommand = new RelayCommand(
                        async x =>
                        {
                            var count = Documents.Count;
                            ClearDocuments();
                            await LoadDocuments();
                        }));
            }
        }

        public RelayCommand NewDocumentCommand
        {
            get
            {
                return _newDocumentCommand
                    ?? (_newDocumentCommand = new RelayCommand(
                        x =>
                        {
                            SelectedDocument = null;
                            EditorViewModel.SetText(new Document() { Id = "replace_with_the_new_document_id" }, HideSystemProperties);
                        }                        ,
                        x =>
                        {
                            // Can create new document if current document is not a new document
                            return !EditorViewModel.IsNewDocument && !EditorViewModel.IsDirty;
                        }));
            }
        }

        public RelayCommand DiscardCommand
        {
            get
            {
                return _discardCommand
                    ?? (_discardCommand = new RelayCommand(
                        x => OnSelectedDocumentChanged(),
                        x => EditorViewModel.IsDirty));
            }
        }

        public RelayCommand SaveDocumentCommand
        {
            get
            {
                return _saveDocumentCommand
                    ?? (_saveDocumentCommand = new RelayCommand(
                        async x =>
                        {
                            var document = await _dbService.UpdateDocument(Connection, Collection.AltLink, EditorViewModel.Content.Text);

                            var description = new DocumentDescription { Id = document.Id, SelfLink = document.SelfLink };

                            if (SelectedDocument == null)
                            {
                                Documents.Add(description);
                            }

                            SelectedDocument = description;
                        },
                        x => EditorViewModel.IsDirty));
            }
        }

        public RelayCommand DeleteDocumentCommand
        {
            get
            {
                return _deleteDocumentCommand
                    ?? (_deleteDocumentCommand = new RelayCommand(
                        async x =>
                        {
                            await _dialogService.ShowMessage("Are you sure...", "Delete Document", null, null, async confirm =>
                            {
                                if (confirm)
                                {
                                    await _dbService.DeleteDocument(Node.Parent.Parent.Parent.Connection, SelectedDocument.SelfLink);

                                    await DispatcherHelper.RunAsync(() =>
                                    {
                                        Documents.Remove(SelectedDocument);
                                        SelectedDocument = null;
                                    });
                                }
                            });
                        }, 
                        x => SelectedDocument != null && !EditorViewModel.IsNewDocument));
            }
        }

        public RelayCommand EditFilterCommand
        {
            get
            {
                return _editFilterCommand
                    ?? (_editFilterCommand = new RelayCommand(
                        x =>
                        {
                            IsEditingFilter = true;
                        }));
            }
        }

        public RelayCommand ApplyFilterCommand
        {
            get
            {
                return _applyFilterCommand
                    ?? (_applyFilterCommand = new RelayCommand(
                        async x =>
                        {
                            IsEditingFilter = false;
                            ClearDocuments();
                            await LoadDocuments();
                        }));
            }
        }

        public RelayCommand CloseFilterCommand
        {
            get
            {
                return _closeFilterCommand
                    ?? (_closeFilterCommand = new RelayCommand(
                        x =>
                        {
                            IsEditingFilter = false;
                        }));
            }
        }

        public RelayCommand SaveLocalCommand
        {
            get
            {
                return _saveLocalCommand ??
                (_saveLocalCommand = new RelayCommand(
                    async x =>
                    {
                        var settings = new SaveFileDialogSettings
                        {
                            DefaultExt = "json",
                            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                            AddExtension = true,
                            FileName = $"{SelectedDocument.Id}.json",
                            OverwritePrompt = true,
                            CheckFileExists = false,
                            Title = "Save document locally"
                        };

                        await _dialogService.ShowSaveFileDialog(settings, async (confirm, result) =>
                        {
                            if (confirm)
                            {
                                await DispatcherHelper.RunAsync(() =>
                                {
                                    File.WriteAllText(result.FileName, EditorViewModel.Content.Text);
                                });
                            }
                        });
                    },
                    x => SelectedDocument != null));
            }
        }

        public double Zoom { get; set; } = 0.5;
        public bool HideSystemProperties { get; set; } = true;

        public void OnHideSystemPropertiesChanged()
        {
            OnSelectedDocumentChanged();
        }

        public bool EnableScanInQuery { get; set; }
        public bool EnableCrossPartitionQuery { get; set; }

        private void ClearDocuments()
        {
            HasMore = false;
            ContinuationToken = null;
            Documents.Clear();
        }
    }
}