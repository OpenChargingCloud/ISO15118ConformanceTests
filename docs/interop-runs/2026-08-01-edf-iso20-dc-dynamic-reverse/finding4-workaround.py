"""FINDING 4, worked around locally so the Dynamic run can continue past it.

evcc/states/wait_for_authorization_setup_response.py walks the authorization services the station
offered and raises NotImplementedError on the first one it does not itself support:

    for _ in payload.authorization_services:
        if _ in self.controller.data_model.authorization_services:
            ... select EIM ...
        else:
            raise NotImplementedError

Our SECC offers **both** EIM and Plug & Charge, deliberately and legally — the EV picks one, and a
PnC-capable car takes PnC. Their EV supports EIM only, so the PnC entry in the list kills the session
even though EIM, which they do support, is right there in the same list.

An EVSE offering PnC alongside EIM is the normal case in the field, so this is not an exotic
combination. The fix on their side is to skip what they cannot use rather than to raise on it.

This patches THEIR copy inside a throwaway container, not our stack.
"""

path = "evcc/states/wait_for_authorization_setup_response.py"
source = open(path).read()

old = ("            else:\n"
       "                raise NotImplementedError\n"
       "                # TODO other cases")
new = ("            else:\n"
       "                continue   # FINDING 4: skip a service we do not support, do not abort on it")

assert old in source, "their handler no longer looks the way this patch expects"

open(path, "w").write(source.replace(old, new))
print("FINDING 4 worked around in", path)
