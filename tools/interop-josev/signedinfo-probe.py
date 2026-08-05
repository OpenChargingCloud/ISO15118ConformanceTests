#!/usr/bin/env python3
"""
Reproduce Josev's exact PnC ``SignedInfo`` signing octets using Josev's own EXI codec.

This is the ground-truth reproduction behind the "SignedInfo signature" root cause
(docs/interop-runs/2026-07-21-iso20-dc-pnc-tls/notes.md,
 EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Interop/JosevPnCSignatureDiag.cs):

Josev encodes the ``SignedInfo`` for signing via ``EXI().to_exi(signed_info, Namespace.XML_DSIG)``.
Inside Josev's ``EXICodec.jar`` the XMLDSig namespace maps to ``BuiltInSchema.XSDCore`` →
``XMLDSIG_Core_Schema_Grammar``, i.e. a grammar built from ``xmldsig-core-schema.xsd`` *standalone*
(NOT the combined V2G_CI_CommonMessages fragment grammar that cbV2G / our codec use). That yields a
one-bit-narrower top-level EXI Fragment element event code, so the whole bitstream is shifted and the
result is 209 bytes vs our/cbV2G 210 bytes — though both decode to the identical SignedInfo.

It reconstructs the exact SignedInfo from the captured live PnC AuthorizationReq
(id="id1", canonical-exi C14N, ecdsa-sha256, sha256 digest = the captured DigestValue) and prints the
EXI hex. Josev's captured 64-byte ECDSA-P256 signature verifies (SHA-256) against these octets.

Run it inside the Josev SECC container (which ships py4j + a JVM + EXICodec.jar):

    docker run --rm --entrypoint /venv/bin/python \
        -v "$PWD/tools/interop-josev/signedinfo-probe.py:/tmp/probe.py:ro" \
        iso15118-secc:latest /tmp/probe.py

Expected: LEN 209 and a hex string equal to
JosevPnCSignatureDiag.JosevStandaloneXmldsigSignedInfoHex.
"""
import base64

from iso15118.shared.settings import load_shared_settings
from iso15118.shared.exi_codec import EXI
from iso15118.shared.exificient_exi_codec import ExificientEXICodec
from iso15118.shared.messages.enums import Namespace
from iso15118.shared.messages.xmldsig import (
    SignedInfo, Reference, Transforms, Transform,
    DigestMethod, CanonicalizationMethod, SignatureMethod,
)

# The captured live PnC AuthorizationReq's DigestValue (base64, over PnC_AReqAuthorizationMode).
CAPTURED_DIGEST_B64 = "ubOqbKGp+UN/pxqyi6k04w2/TFMAqFy/dJ6LIKa3Rw0="

load_shared_settings()
EXI().set_exi_codec(ExificientEXICodec())

signed_info = SignedInfo(
    canonicalization_method=CanonicalizationMethod(algorithm="http://www.w3.org/TR/canonical-exi/"),
    signature_method=SignatureMethod(algorithm="http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256"),
    reference=[Reference(
        uri="#id1",
        transforms=Transforms(transform=[Transform(algorithm="http://www.w3.org/TR/canonical-exi/")]),
        digest_method=DigestMethod(algorithm="http://www.w3.org/2001/04/xmlenc#sha256"),
        digest_value=base64.b64decode(CAPTURED_DIGEST_B64),
    )],
)

octets = EXI().to_exi(signed_info, Namespace.XML_DSIG)
print("HEX", octets.hex())
print("LEN", len(octets))
