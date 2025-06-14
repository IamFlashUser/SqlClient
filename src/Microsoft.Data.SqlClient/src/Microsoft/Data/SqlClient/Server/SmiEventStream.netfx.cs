// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NETFRAMEWORK

using System;

namespace Microsoft.Data.SqlClient.Server
{
    // @TODO: Is this implemented *anywhere*?
    internal abstract class SmiEventStream : IDisposable
    {
        internal abstract bool HasEvents { get; }

        internal abstract void Close(SmiEventSink sink);

        [Obsolete("Use Close instead.")]
        public virtual void Dispose()
        {
            // Obsoleting from SMI -- use Close instead.
            //  Intended to be removed (along with inheriting IDisposable) prior to RTM.

            // Implement body with throw because there are only a couple of ways to get to this code:
            //  1) Client is calling this method even though the server negotiated for V3+ and dropped support for V2-.
            //  2) Server didn't implement V2- on some interface and negotiated V2-.
            Common.ADP.InternalError(Common.ADP.InternalErrorCode.UnimplementedSMIMethod);
        }

        internal abstract void ProcessEvent(SmiEventSink sink);
    }
}

#endif
