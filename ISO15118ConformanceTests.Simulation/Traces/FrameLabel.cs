/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using cloud.charging.open.protocols.ISO15118.AppProtocol;
using cloud.charging.open.protocols.ISO15118_2.Generated;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.Traces;

/// <summary>
/// What a frame <i>is</i>, in words: the message name, and the response code when it carries one.
/// </summary>
/// <remarks>
/// <para>
/// Only ever used for reading — trace labels, frame logs, flow reports. Nothing is ever checked against
/// it; the frame bytes are the oracle. So a frame that will not decode is labelled rather than fatal: the
/// bytes are still worth recording, and a codec that cannot read back what just crossed the wire is a
/// finding for a different test.
/// </para>
/// <para>
/// It lives apart from <see cref="SessionTrace"/> because the interop recorder needs the same answer on
/// the path where a trace is <i>not</i> built. That path is the one a session that went wrong takes, and
/// it was the one without names — a frame log of payload types and hex, for exactly the runs where the
/// sequence of messages is the first thing anybody wants to see.
/// </para>
/// </remarks>
public static class FrameLabel
{

    /// <param name="isSap">Whether this frame is the SupportedAppProtocol handshake. It shares payload id
    /// <c>0x8001</c> with every -2 message and is told apart by session phase, never by bytes — phase,
    /// here, being position: SAP is the first exchange.</param>
    public static (String Message, String? ResponseCode) Describe(Byte[] frame, Boolean isSap)
    {
        try
        {

            if (isSap)
            {
                var sap = SupportedAppProtocolCodec.DecodeAny(frame.AsSpan(V2GTPCodec.HeaderSize), out _);
                return (sap.GetType().Name, ResponseCodeOf(sap));
            }

            if (!V2GTPDispatcher.TryDecode(frame, out _, out var message, out _) || message is null)
                return ("undecodable", null);

            // -2 wraps everything in V2G_Message; the interesting name is the body element. The -20 sets
            // decode straight to the concrete message type.
            var element = message is V2G_Message v2g ? v2g.Body.BodyElement : message;

            return element is null
                       ? ("V2G_Message(empty body)", null)
                       : (element.GetType().Name, ResponseCodeOf(element));

        }
        catch (Exception e)
        {
            return ($"undecodable({e.GetType().Name})", null);
        }
    }


    /// <summary>The message name without the generator's <c>Type</c> suffix — <c>SessionSetupReq</c>
    /// rather than <c>SessionSetupReqType</c>. What a human writes, and what a counterparty's own
    /// vocabulary can be compared against.</summary>
    public static String Canonical(String message)
        => message.EndsWith("Type", StringComparison.Ordinal) && message.Length > 4
               ? message[..^4]
               : message;


    /// <summary>
    /// Read off the decoded message by name, because there is no shared base carrying it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reflection, deliberately kept to this one reading-only method rather than pushed into the
    /// generated model: a hierarchy invented here so a report could be prettier would be a wire-model
    /// change made for a log file's benefit.
    /// </para>
    /// <para>
    /// Two names, because the hand-written SupportedAppProtocol codec calls it <c>Code</c> while the
    /// generated -2/-20 messages call it <c>ResponseCode</c>. Worth the second lookup: the handshake's
    /// code is the <i>first</i> thing an interop session can fail on, and a report that silently left it
    /// blank would leave the blank exactly where the answer usually is. Restricted to enum-typed
    /// properties so a <c>Code</c> that means something else cannot be picked up by accident.
    /// </para>
    /// </remarks>
    // 'object', not 'Object': the -2 schema has an xmldsig type of that name in scope here.
    private static String? ResponseCodeOf(object message)
    {

        foreach (var name in new[] { "ResponseCode", "Code" })
        {

            if (message.GetType().GetProperty(name) is not { } property)
                continue;

            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (type.IsEnum)
                return property.GetValue(message)?.ToString();

        }

        return null;

    }

}
