#!/usr/bin/env python3
"""
Reproduce Josev's -20 CertificateInstallationReq EXI bytes (the frame our SECC failed to decode with
"Unknown document index 14") using Josev's own EXIficient codec, from the exact values captured in the
2026-07-22 live probe (/tmp/evcc-certinstall-probe.log).

Also encodes a SessionSetupReq for sanity (that message decodes fine on our side), so the two hex dumps
can be byte-diffed against our encoder's output to localise where the document grammars diverge.

Run inside the Josev image (py4j + JVM + EXICodec.jar):

    docker run --rm --entrypoint /venv/bin/python \
        -v "$PWD/tools/interop-josev/certinstall-probe.py:/tmp/probe.py:ro" \
        iso15118-secc:latest /tmp/probe.py
"""
import base64

from iso15118.shared.settings import load_shared_settings
from iso15118.shared.exi_codec import EXI
from iso15118.shared.exificient_exi_codec import ExificientEXICodec
from iso15118.shared.messages.enums import Namespace
from iso15118.shared.messages.iso15118_20.common_messages import (
    CertificateInstallationReq, SignedCertificateChain, SubCertificates,
    RootCertificateIDList, SessionSetupReq,
)
from iso15118.shared.messages.iso15118_20.common_types import MessageHeader
from iso15118.shared.messages.xmldsig import (
    Signature, SignedInfo, SignatureValue, Reference, Transforms, Transform,
    DigestMethod, CanonicalizationMethod, SignatureMethod, X509IssuerSerial,
)

load_shared_settings()
EXI().set_exi_codec(ExificientEXICodec())

# Values captured from the live probe log (evcc-certinstall-probe.log, 2026-07-22 15:40:55).
LEAF = "MIIB4DCCAYagAwIBAgICMEAwCgYIKoZIzj0EAwIwRzESMBAGA1UEAwwJT0VNU3ViQ0EyMQ8wDQYDVQQKDAZTd2l0Y2gxCzAJBgNVBAYTAlVLMRMwEQYKCZImiZPyLGQBGRYDT0VNMB4XDTI2MDcyMTExNTMyMVoXDTMwMDcyMDExNTMyMVowSTEUMBIGA1UEAwwLT0VNUHJvdkNlcnQxDzANBgNVBAoMBlN3aXRjaDELMAkGA1UEBhMCVUsxEzARBgoJkiaJk/IsZAEZFgNPRU0wWTATBgcqhkjOPQIBBggqhkjOPQMBBwNCAAQj8aufUWYEefCLZDU00mzi4gFZ3aam7qMfl1Z9uxlYe4D/FJMldIQjIIBvj9FhRv3XoPUeY6EkBYSKyM9PfIH1o2AwXjAMBgNVHRMBAf8EAjAAMA4GA1UdDwEB/wQEAwIDiDAdBgNVHQ4EFgQU5+mDkwSxgW64xnAvVMDJ2xu+AgAwHwYDVR0jBBgwFoAUx7VzMJWAj4Lu8EsNc3BkwwPasf0wCgYIKoZIzj0EAwIDSAAwRQIhANBNP8FbFyj7OWDd81k6E0GAkO94gGMVMLHh7hJ4jm01AiB46IZxvwO41uvK1hloZb5WnR/zWNh4uHWZJwlDknO1ig=="
SUB2 = "MIIB5DCCAYqgAwIBAgICMD8wCgYIKoZIzj0EAwIwRzESMBAGA1UEAwwJT0VNU3ViQ0ExMQ8wDQYDVQQKDAZTd2l0Y2gxCzAJBgNVBAYTAlVLMRMwEQYKCZImiZPyLGQBGRYDT0VNMB4XDTI2MDcyMTExNTMyMFoXDTMwMDcyMDExNTMyMFowRzESMBAGA1UEAwwJT0VNU3ViQ0EyMQ8wDQYDVQQKDAZTd2l0Y2gxCzAJBgNVBAYTAlVLMRMwEQYKCZImiZPyLGQBGRYDT0VNMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEwkS99Os/pms5BDv7dzpivMI/LBaXtQ8ZGT3ZfKza8FRMzsMdqNFIYnpdZZEn+htbODWhEEigeofJn/iS94V3OaNmMGQwEgYDVR0TAQH/BAgwBgEB/wIBADAOBgNVHQ8BAf8EBAMCAQYwHQYDVR0OBBYEFMe1czCVgI+C7vBLDXNwZMMD2rH9MB8GA1UdIwQYMBaAFOnxC3BoMEBd438dybLiHgEjnA9zMAoGCCqGSM49BAMCA0gAMEUCIQCSpzXsgqOuzJSyi8vO/v57wBOHpPMUJva+qepnV+yYNgIgE7pfyvnPy1sCOHgveAWtebiQQpdAdTAf2x1nOvyFNKg="
SUB1 = "MIIB5DCCAYqgAwIBAgICMD4wCgYIKoZIzj0EAwIwRzESMBAGA1UEAwwJT0VNUm9vdENBMQ8wDQYDVQQKDAZTd2l0Y2gxCzAJBgNVBAYTAlVLMRMwEQYKCZImiZPyLGQBGRYDT0VNMB4XDTI2MDcyMTExNTMyMFoXDTMwMDcyMDExNTMyMFowRzESMBAGA1UEAwwJT0VNU3ViQ0ExMQ8wDQYDVQQKDAZTd2l0Y2gxCzAJBgNVBAYTAlVLMRMwEQYKCZImiZPyLGQBGRYDT0VNMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAErtbSGfkyuzIESvzYdyf4WQWj+3H3JKutZO+AzeV2avhFSKo4VoUi4hj868PzUnwb70FYxdh1fTXAKRhTAvu6RKNmMGQwEgYDVR0TAQH/BAgwBgEB/wIBATAOBgNVHQ8BAf8EBAMCAQYwHQYDVR0OBBYEFOnxC3BoMEBd438dybLiHgEjnA9zMB8GA1UdIwQYMBaAFBLJt1yxgoX0BW/56v4y+YfDwMc6MAoGCCqGSM49BAMCA0gAMEUCIQCfl0Ypqicz+Nloo74+MhWZRRr+51tZGVTvM6IboCx4uQIgCEeXsieakDq23kriyWtF7cW2GlUj7oZi5HwvKxfum8A="

signature = Signature(
    signed_info=SignedInfo(
        canonicalization_method=CanonicalizationMethod(algorithm="http://www.w3.org/TR/canonical-exi/"),
        signature_method=SignatureMethod(algorithm="http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256"),
        reference=[Reference(
            uri="#id1",
            transforms=Transforms(transform=[Transform(algorithm="http://www.w3.org/TR/canonical-exi/")]),
            digest_method=DigestMethod(algorithm="http://www.w3.org/2001/04/xmlenc#sha256"),
            digest_value=base64.b64decode("CtBDY/O/MiKlYVxJV7UlQH5VOo1MYhCcukSTaJxW7EI="),
        )],
    ),
    signature_value=SignatureValue(value=base64.b64decode("qqPyrSg7P+G2PkKocziBRaz+RpVGhZNfpOSDIlR51gL8zrjMP6qeOzJPs8VKWOuVC8jT8g8lWq77adyHAxSNDA==")),
)

req = CertificateInstallationReq(
    header=MessageHeader(session_id="B3B6633F1629E7EE", timestamp=1784734855, signature=signature),
    oem_prov_cert_chain=SignedCertificateChain(
        id="id1",
        certificate=base64.b64decode(LEAF),
        sub_certificates=SubCertificates(certificates=[base64.b64decode(SUB2), base64.b64decode(SUB1)]),
    ),
    root_cert_id_list=RootCertificateIDList(root_cert_ids=[
        X509IssuerSerial(x509_issuer_name="<Name(CN=V2GRootCA,O=Switch,C=UK,DC=V2G)>", x509_serial_number=12345),
    ]),
    max_contract_cert_chains=3,
)

octets = EXI().to_exi(req, Namespace.ISO_V20_COMMON_MSG)
print("CERTINSTALL_HEX", octets.hex())
print("CERTINSTALL_LEN", len(octets))

setup = SessionSetupReq(
    header=MessageHeader(session_id="B3B6633F1629E7EE", timestamp=1784734855),
    evcc_id="WMIV1234567890ABCDEX",
)
octets2 = EXI().to_exi(setup, Namespace.ISO_V20_COMMON_MSG)
print("SESSIONSETUP_HEX", octets2.hex())
