﻿using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using static Nitra.ClientServer.Messages.AsyncServerMessage;
using Microsoft.VisualStudio.Text;
using System.Windows.Threading;
using Nitra.ClientServer.Messages;
using System.Collections.Immutable;
using Nitra.VisualStudio.Highlighting;
using System.Diagnostics;
using Nitra.VisualStudio.CompilerMessages;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Windows.Media;
using System.IO;
using System.Xml.Linq;
using System.Text;
using Microsoft.VisualStudio.Language.Intellisense;

namespace Nitra.VisualStudio.Models
{
  /// <summary>
  /// Represent file in a text editor. An instance of this class is created for opened (in editors) files
  /// that at least once were visible on the screen. If the user closes a tab, its associated the FileModel is destroyed.
  /// </summary>
  internal class FileModel : IDisposable
  {
    public const int KindCount = 3;
    readonly ITextBuffer _textBuffer;
    public ServerModel Server { get; }
    public FileId Id { get; }
    public IVsHierarchy Hierarchy { get; }
    public string FullPath { get; }
    public string Ext { get; }
    public CompilerMessage[][] CompilerMessages { get; private set; }
    public ITextSnapshot[] CompilerMessagesSnapshots { get; private set; }
    ErrorListProvider[] _errorListProviders;

    readonly Dictionary<IWpfTextView, TextViewModel> _textViewModelsMap = new Dictionary<IWpfTextView, TextViewModel>();
    TextViewModel _activeTextViewModelOpt;
    TextViewModel _mouseHoverTextViewModelOpt;
    bool _fileIsRemoved;
    ICompletionSession _completionSession;
    VersionedPos _caretPosition;

    public FileModel(FileId id, ITextBuffer textBuffer, ServerModel server, Dispatcher dispatcher, IVsHierarchy hierarchy, string fullPath)
    {
      Hierarchy = hierarchy;
      FullPath = fullPath;
      Ext = Path.GetExtension(fullPath).ToLowerInvariant();
      Id = id;
      Server = server;
      _textBuffer = textBuffer;

      var snapshot = textBuffer.CurrentSnapshot;
      var empty = new CompilerMessage[0];
      CompilerMessages = new CompilerMessage[KindCount][] { empty, empty, empty };
      CompilerMessagesSnapshots = new ITextSnapshot[KindCount] { snapshot, snapshot, snapshot };
      _errorListProviders = new ErrorListProvider[KindCount] { null, null, null };

      server.Client.ResponseMap[id] = msg => dispatcher.BeginInvoke(DispatcherPriority.Normal,
        new Action<AsyncServerMessage>(msg2 => Response(msg2)), msg);

      server.Add(this);

      textBuffer.Changed += TextBuffer_Changed;
    }

    public TextViewModel[] TextViewModels => _textViewModelsMap.Values.ToArray();

    public bool IsOnScreen => _textViewModelsMap.Count > 0;

    public void CaretPositionChanged(VersionedPos position)
    {
      _caretPosition = position;

      var server = this.Server;

      if (server.IsLoaded)
        server.Client.Send(new ClientMessage.SetCaretPos(GetProjectId(), Id, position));
    }

    public TextViewModel GetOrAdd(IWpfTextView wpfTextView)
    {
      if (!_textViewModelsMap.TryGetValue(wpfTextView, out var textViewModel))
      {
        var isOnScreen = IsOnScreen;
        _textViewModelsMap.Add(wpfTextView, textViewModel = new TextViewModel(wpfTextView, this));

        if (!isOnScreen)
          OnSwhow();
      }

      return textViewModel;
    }

    void OnSwhow()
    {
      var server = this.Server;

      if (server.IsLoaded)
        server.Client.Send(new ClientMessage.FileActivated(GetProjectId(), Id, _textBuffer.CurrentSnapshot.Version.Convert()));
    }

    public void Remove(IWpfTextView wpfTextView)
    {
      if (_textViewModelsMap.TryGetValue(wpfTextView, out var textViewModel))
      {
        var isWasOnScreen = IsOnScreen;

        if (textViewModel == _activeTextViewModelOpt)
          _activeTextViewModelOpt = null;
        if (textViewModel == _mouseHoverTextViewModelOpt)
          _mouseHoverTextViewModelOpt = null;
        textViewModel.Dispose();
        _textViewModelsMap.Remove(wpfTextView);

        if (isWasOnScreen && !IsOnScreen)
          OnHide();
      }

      return;
    }

    public void OnHide()
    {
      var server = this.Server;

      if (!_fileIsRemoved && server.IsLoaded)
        server.Client.Send(new ClientMessage.FileDeactivated(GetProjectId(), Id));
    }

    internal void ViewActivated(TextViewModel textViewModel)
    {
      _activeTextViewModelOpt = textViewModel;
    }

    internal void OnMouseHover(TextViewModel textViewModel)
    {
      _mouseHoverTextViewModelOpt = textViewModel;
    }

    void TextBuffer_Changed(object sender, TextContentChangedEventArgs e)
    {
      var textBuffer = (ITextBuffer)sender;
      var newVersion = e.AfterVersion.Convert();
      var changes = e.Changes;
      if (newVersion != _caretPosition.Version)
      {
      }
      var fileModel = textBuffer.Properties.GetProperty<FileModel>(Constants.FileModelKey);
      var id = fileModel.Id;

      if (changes.Count == 1)
        Server.Client.Send(new ClientMessage.FileChanged(id, newVersion, VsUtils.Convert(changes[0]), _caretPosition));
      else
      {
        var builder = ImmutableArray.CreateBuilder<FileChange>(changes.Count);

        foreach (var change in changes)
          builder.Add(VsUtils.Convert(change));

        Server.Client.Send(new ClientMessage.FileChangedBatch(id, newVersion, builder.MoveToImmutable(), _caretPosition));
      }
    }

    public ProjectId GetProjectId()
    {
      var project = this.Hierarchy.GetProject();
      return new ProjectId(this.Server.Client.StringManager.GetId(project.FullName));
    }

    internal void Remove()
    {
      _fileIsRemoved = true;
      Server.Client.Send(new ClientMessage.FileUnloaded(GetProjectId(), Id));
    }

    void Response(AsyncServerMessage msg)
    {
      Debug.Assert(msg.FileId >= 0);
      ITextBuffer textBuffer = _textBuffer;

      switch (msg)
      {
        case OutliningCreated outlining:
          var tegget = (OutliningTagger)textBuffer.Properties.GetProperty(Constants.OutliningTaggerKey);
          tegget.Update(outlining);
          break;
        case KeywordsHighlightingCreated keywordHighlighting:
          UpdateSpanInfos(HighlightingType.Keyword, keywordHighlighting.spanInfos, keywordHighlighting.Version);
          break;
        case SymbolsHighlightingCreated symbolsHighlighting:
          UpdateSpanInfos(HighlightingType.Symbol, symbolsHighlighting.spanInfos, symbolsHighlighting.Version);
          break;
        case MatchedBrackets matchedBrackets:
          if (_activeTextViewModelOpt == null)
            return;

          _activeTextViewModelOpt.Update(matchedBrackets);
          break;
        case ParsingMessages parsingMessages:
          UpdateCompilerMessages(0, parsingMessages.messages, parsingMessages.Version);
          break;
        case MappingMessages mappingMessages:
          UpdateCompilerMessages(1, mappingMessages.messages, mappingMessages.Version);
          break;
        case SemanticAnalysisMessages semanticAnalysisMessages:
          UpdateCompilerMessages(2, semanticAnalysisMessages.messages, semanticAnalysisMessages.Version);
          break;
        case Hint hint:
          _mouseHoverTextViewModelOpt?.ShowHint(hint);
          break;
        case CompleteWord completeWord:
          if (_completionSession != null)
          {
            _completionSession.Properties[Constants.NitraCompleteWord] = completeWord;
            if (_completionSession.IsStarted)
              _completionSession.Recalculate();
            else
              _completionSession.Start();
          }
          break;
      }
    }

    internal void SetCompletionSession(ICompletionSession session, SnapshotPoint caretPos)
    {
      _completionSession = session;
      var snapshot = caretPos.Snapshot;
      var version = snapshot.Version.Convert();
      var triggerPoint = session.GetTriggerPoint(session.TextView.TextBuffer);
      Server.Client.Send(new ClientMessage.CompleteWord(GetProjectId(), Id, version, triggerPoint.GetPoint(snapshot).Position));
      session.Dismissed += CompletionSessionDismissed;
    }

    private void CompletionSessionDismissed(object sender, EventArgs e)
    {
      if (_completionSession == null)
        return;
      _completionSession.Dismissed -= CompletionSessionDismissed;
      _completionSession.Properties[Constants.NitraCompleteWord] = null;
      _completionSession = null;
      Server.Client.Send(new ClientMessage.CompleteWordDismiss(GetProjectId(), Id));
    }

    internal Brush SpanClassToBrush(string spanClass, IWpfTextView _wpfTextView)
    {
      var classifierOpt = GetClassifierOpt();
      if (classifierOpt == null)
        return Server.SpanClassToBrush(spanClass);

      return classifierOpt.SpanClassToBrush(spanClass, _wpfTextView);
    }

    private void UpdateCompilerMessages(int index, CompilerMessage[] messages, int version)
    {
      var snapshot = _textBuffer.CurrentSnapshot;

      if (snapshot.Version.VersionNumber != version + 1)
        return;

      CompilerMessages[index] = messages;
      CompilerMessagesSnapshots[index] = snapshot;

      CompilerMessagesTagger tegger;
      if (_textBuffer.Properties.TryGetProperty<CompilerMessagesTagger>(Constants.CompilerMessagesTaggerKey, out tegger))
        tegger.Update();

      var errorListProvider = _errorListProviders[index];
      var noTasks = errorListProvider == null || errorListProvider.Tasks.Count == 0;
      if (!(messages.Length == 0 && noTasks))
      {
        if (errorListProvider == null)
        {
          _errorListProviders[index] = errorListProvider = new NitraErrorListProvider(Server.ServiceProvider);
          errorListProvider.MaintainInitialTaskOrder = true;
          errorListProvider.DisableAutoRoute = true;
        }
        errorListProvider.SuspendRefresh();
        try
        {
          var tasks = errorListProvider.Tasks;
          tasks.Clear();
          foreach (var msg in messages)
          {
            AddTask(snapshot, errorListProvider, msg);
          }
        }
        finally
        {
          errorListProvider.ResumeRefresh();
        }
      }
    }

    private void AddTask(ITextSnapshot snapshot, ErrorListProvider errorListProvider, CompilerMessage msg)
    {
      var startPos = msg.Location.Span.StartPos;
      if (startPos > snapshot.Length)
        return;

      var line = snapshot.GetLineFromPosition(startPos);
      var col = startPos - line.Start.Position;
      var text = ToText(msg.Text);
      var task = new ErrorTask()
      {
        Text = text,
        Category = TaskCategory.CodeSense,
        ErrorCategory = ConvertMessageType(msg.Type),
        Priority = TaskPriority.High,
        HierarchyItem = Hierarchy,
        Line = line.LineNumber,
        Column = col,
        Document = FullPath,
      };

      task.Navigate += Task_Navigate;

      errorListProvider.Tasks.Add(task);

      foreach (var nested in msg.NestedMessages)
        AddTask(snapshot, errorListProvider, nested);
    }

    static string ToText(string text)
    {
      if (!text.StartsWith("<hint>", StringComparison.InvariantCultureIgnoreCase))
        return text;

      var builder = new StringBuilder();
      XmlToString(builder, XElement.Parse(text));
      return builder.ToString();
    }

    static void XmlToString(StringBuilder builder, XContainer container)
    {
      foreach (var n in container.Nodes())
      {
        switch (n)
        {
          case XElement e when e.Name == "br" || e.Name == "bl": builder.AppendLine(); break;
          case XContainer c: XmlToString(builder, c); break;
          case XText t: builder.Append(t.Value); break;
        }
      }
    }

    TextViewModel GetTextViewModel()
    {
      if (_activeTextViewModelOpt != null)
        return _activeTextViewModelOpt;

      foreach (TextViewModel textViewModel in _textViewModelsMap.Values)
        return textViewModel;

      return null;
    }

    private void Task_Navigate(object sender, EventArgs e)
    {
      var task = (ErrorTask)sender;


      var textViewModel = GetTextViewModel();
      if (textViewModel == null)
      {
        VsUtils.NavigateTo(Server.ServiceProvider, FullPath, task.Line, task.Column);
        return;
      }

      textViewModel.Navigate(task.Line, task.Column);
    }

    static TaskErrorCategory ConvertMessageType(CompilerMessageType type)
    {
      switch (type)
      {
        case CompilerMessageType.FatalError:
          return TaskErrorCategory.Error;
        case CompilerMessageType.Error:
          return TaskErrorCategory.Error;
        case CompilerMessageType.Warning:
          return TaskErrorCategory.Warning;
        case CompilerMessageType.Hint:
          return TaskErrorCategory.Message;
        default:
          return TaskErrorCategory.Error;
      }
    }

    NitraEditorClassifier GetClassifierOpt()
    {
      NitraEditorClassifier classifier;
      _textBuffer.Properties.TryGetProperty(Constants.NitraEditorClassifierKey, out classifier);
      return classifier;
    }

    void UpdateSpanInfos(HighlightingType highlightingType, ImmutableArray<SpanInfo> spanInfos, FileVersion version)
    {
      var classifierOpt = GetClassifierOpt();
      if (classifierOpt == null)
        return;
      classifierOpt.Update(highlightingType, spanInfos, version);
    }

    public override string ToString()
    {
      return Path.GetFileName(FullPath) + " [" + _textViewModelsMap.Count + " view(s)]";
    }

    public void Dispose()
    {
      var textViews = _textViewModelsMap.Keys.ToArray();

      foreach (var textView in textViews)
        Remove(textView);

      _textBuffer.Changed -= TextBuffer_Changed;
      foreach (var errorListProvider in _errorListProviders)
        if (errorListProvider != null)
          errorListProvider.Dispose();
      _errorListProviders = null;
      var client = Server.Client;
      client.ResponseMap.TryRemove(Id, out var value);
      _textBuffer.Properties.RemoveProperty(Constants.FileModelKey);
      Server.Remove(this);
    }
  }
}
