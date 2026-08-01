"""FINDING 1, worked around locally so the run can continue past it.

secc/states/process_service_discovery_request.py dereferences payload.supported_service_ids
unconditionally. In ISO 15118-20 that element is OPTIONAL —

    <xs:element name="SupportedServiceIDs" type="ServiceIDListType" minOccurs="0"/>
        (V2G_CI_CommonMessages.xsd)

— and omitting it means "no filter, tell me everything". Our EVCC omits it, which is legal, and their
session dies with:

    AttributeError: 'NoneType' object has no attribute 'service_id'

This patches THEIR copy inside a throwaway container, not our stack. A missing filter is treated as
"any", which is what the standard says it means.
"""

path = "secc/states/process_service_discovery_request.py"
source = open(path).read()

# The fallback is [2] — plain DC — and not [6, 2]. It has to match what the EV goes on to select, and
# picking 6 (DC_BPT) here makes their DC_ChargeParameterDiscovery handler read BPT-only fields
# (evmaximum_discharge_power) out of a plain-DC request, which then fails for a reason that is this
# patch's doing rather than theirs. A workaround that manufactures the next failure is worse than none.
old = "        if 6 in payload.supported_service_ids.service_id:"
new = ("        ids = (payload.supported_service_ids.service_id\n"
       "               if payload.supported_service_ids is not None else [2])\n"
       "        if 6 in ids:")

assert old in source, "their handler no longer looks the way this patch expects"

source = source.replace(old, new).replace(
    "        elif 2 in payload.supported_service_ids.service_id:", "        elif 2 in ids:")

open(path, "w").write(source)
print("FINDING 1 worked around in", path)
