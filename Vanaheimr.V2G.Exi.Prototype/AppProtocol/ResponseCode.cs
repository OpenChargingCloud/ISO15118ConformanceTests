namespace Vanaheimr.V2G.AppProtocol;

public enum ResponseCode : byte
{
    OK_SuccessfulNegotiation                       = 0,
    OK_SuccessfulNegotiationWithMinorDeviation     = 1,
    Failed_NoNegotiation                           = 2,
}
