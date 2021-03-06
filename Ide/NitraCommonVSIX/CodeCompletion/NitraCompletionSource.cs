﻿using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using Nitra.ClientServer.Messages;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nitra.VisualStudio.CodeCompletion
{
  class NitraCompletionSource : ICompletionSource
  {
    readonly ITextBuffer                   _textBuffer;
             bool                          _isDisposed;
             NitraCompletionSourceProvider _sourceProvider;

    public NitraCompletionSource(NitraCompletionSourceProvider sourceProvider, ITextBuffer textBuffer)
    {
      _sourceProvider = sourceProvider;
      _textBuffer     = textBuffer;
    }

    void ICompletionSource.AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
    {
      var fileModel = VsUtils.TryGetFileModel(_textBuffer);

      if (fileModel == null)
        return;

      var msg = (AsyncServerMessage.CompleteWord)session.Properties[Constants.NitraCompleteWord];

      if (msg == null)
        return;

      var span          = msg.replacementSpan;
      var snapshot      = _textBuffer.CurrentSnapshot;
      var version       = snapshot.Version.Convert();

      if (msg.Version != version)
      {
        return;
      }

      var applicableTo = snapshot.CreateTrackingSpan(new Span(span.StartPos, span.Length), SpanTrackingMode.EdgeInclusive);

      var completionSet = new NitraCompletionSet(applicableTo, session, snapshot);
      completionSet.Recalculate();
      completionSets.Add(completionSet);
    }

    public void Dispose()
    {
      if (!_isDisposed)
      {
        GC.SuppressFinalize(this);
        _isDisposed = true;
      }
    }
  }
}
