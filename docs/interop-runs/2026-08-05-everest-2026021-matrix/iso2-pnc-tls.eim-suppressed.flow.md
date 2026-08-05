# Session flow

372 request frame(s), 372 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 3 | PaymentServiceSelectionReq | PaymentServiceSelectionRes | OK |
| 4 | PaymentDetailsReq | PaymentDetailsRes | OK |
| 5 | AuthorizationReq | AuthorizationRes | OK |
| 6 | AuthorizationReq | AuthorizationRes | OK |
| 7 | AuthorizationReq | AuthorizationRes | OK |
| 8 | AuthorizationReq | AuthorizationRes | OK |
| 9 | AuthorizationReq | AuthorizationRes | OK |
| 10 | AuthorizationReq | AuthorizationRes | OK |
| 11 | AuthorizationReq | AuthorizationRes | OK |
| 12 | AuthorizationReq | AuthorizationRes | OK |
| 13 | AuthorizationReq | AuthorizationRes | OK |
| 14 | AuthorizationReq | AuthorizationRes | OK |
| 15 | AuthorizationReq | AuthorizationRes | OK |
| 16 | AuthorizationReq | AuthorizationRes | OK |
| 17 | AuthorizationReq | AuthorizationRes | OK |
| 18 | AuthorizationReq | AuthorizationRes | OK |
| 19 | AuthorizationReq | AuthorizationRes | OK |
| 20 | AuthorizationReq | AuthorizationRes | OK |
| 21 | AuthorizationReq | AuthorizationRes | OK |
| 22 | AuthorizationReq | AuthorizationRes | OK |
| 23 | AuthorizationReq | AuthorizationRes | OK |
| 24 | AuthorizationReq | AuthorizationRes | OK |
| 25 | AuthorizationReq | AuthorizationRes | OK |
| 26 | AuthorizationReq | AuthorizationRes | OK |
| 27 | AuthorizationReq | AuthorizationRes | OK |
| 28 | AuthorizationReq | AuthorizationRes | OK |
| 29 | AuthorizationReq | AuthorizationRes | OK |
| 30 | AuthorizationReq | AuthorizationRes | OK |
| 31 | AuthorizationReq | AuthorizationRes | OK |
| 32 | AuthorizationReq | AuthorizationRes | OK |
| 33 | AuthorizationReq | AuthorizationRes | OK |
| 34 | AuthorizationReq | AuthorizationRes | OK |
| 35 | AuthorizationReq | AuthorizationRes | OK |
| 36 | AuthorizationReq | AuthorizationRes | OK |
| 37 | AuthorizationReq | AuthorizationRes | OK |
| 38 | AuthorizationReq | AuthorizationRes | OK |
| 39 | AuthorizationReq | AuthorizationRes | OK |
| 40 | AuthorizationReq | AuthorizationRes | OK |
| 41 | AuthorizationReq | AuthorizationRes | OK |
| 42 | AuthorizationReq | AuthorizationRes | OK |
| 43 | AuthorizationReq | AuthorizationRes | OK |
| 44 | AuthorizationReq | AuthorizationRes | OK |
| 45 | AuthorizationReq | AuthorizationRes | OK |
| 46 | AuthorizationReq | AuthorizationRes | OK |
| 47 | AuthorizationReq | AuthorizationRes | OK |
| 48 | AuthorizationReq | AuthorizationRes | OK |
| 49 | AuthorizationReq | AuthorizationRes | OK |
| 50 | AuthorizationReq | AuthorizationRes | OK |
| 51 | AuthorizationReq | AuthorizationRes | OK |
| 52 | AuthorizationReq | AuthorizationRes | OK |
| 53 | AuthorizationReq | AuthorizationRes | OK |
| 54 | AuthorizationReq | AuthorizationRes | OK |
| 55 | AuthorizationReq | AuthorizationRes | OK |
| 56 | AuthorizationReq | AuthorizationRes | OK |
| 57 | AuthorizationReq | AuthorizationRes | OK |
| 58 | AuthorizationReq | AuthorizationRes | OK |
| 59 | AuthorizationReq | AuthorizationRes | OK |
| 60 | AuthorizationReq | AuthorizationRes | OK |
| 61 | AuthorizationReq | AuthorizationRes | OK |
| 62 | AuthorizationReq | AuthorizationRes | OK |
| 63 | AuthorizationReq | AuthorizationRes | OK |
| 64 | AuthorizationReq | AuthorizationRes | OK |
| 65 | AuthorizationReq | AuthorizationRes | OK |
| 66 | AuthorizationReq | AuthorizationRes | OK |
| 67 | AuthorizationReq | AuthorizationRes | OK |
| 68 | AuthorizationReq | AuthorizationRes | OK |
| 69 | AuthorizationReq | AuthorizationRes | OK |
| 70 | AuthorizationReq | AuthorizationRes | OK |
| 71 | AuthorizationReq | AuthorizationRes | OK |
| 72 | AuthorizationReq | AuthorizationRes | OK |
| 73 | AuthorizationReq | AuthorizationRes | OK |
| 74 | AuthorizationReq | AuthorizationRes | OK |
| 75 | AuthorizationReq | AuthorizationRes | OK |
| 76 | AuthorizationReq | AuthorizationRes | OK |
| 77 | AuthorizationReq | AuthorizationRes | OK |
| 78 | AuthorizationReq | AuthorizationRes | OK |
| 79 | AuthorizationReq | AuthorizationRes | OK |
| 80 | AuthorizationReq | AuthorizationRes | OK |
| 81 | AuthorizationReq | AuthorizationRes | OK |
| 82 | AuthorizationReq | AuthorizationRes | OK |
| 83 | AuthorizationReq | AuthorizationRes | OK |
| 84 | AuthorizationReq | AuthorizationRes | OK |
| 85 | AuthorizationReq | AuthorizationRes | OK |
| 86 | AuthorizationReq | AuthorizationRes | OK |
| 87 | AuthorizationReq | AuthorizationRes | OK |
| 88 | AuthorizationReq | AuthorizationRes | OK |
| 89 | AuthorizationReq | AuthorizationRes | OK |
| 90 | AuthorizationReq | AuthorizationRes | OK |
| 91 | AuthorizationReq | AuthorizationRes | OK |
| 92 | AuthorizationReq | AuthorizationRes | OK |
| 93 | AuthorizationReq | AuthorizationRes | OK |
| 94 | AuthorizationReq | AuthorizationRes | OK |
| 95 | AuthorizationReq | AuthorizationRes | OK |
| 96 | AuthorizationReq | AuthorizationRes | OK |
| 97 | AuthorizationReq | AuthorizationRes | OK |
| 98 | AuthorizationReq | AuthorizationRes | OK |
| 99 | AuthorizationReq | AuthorizationRes | OK |
| 100 | AuthorizationReq | AuthorizationRes | OK |
| 101 | AuthorizationReq | AuthorizationRes | OK |
| 102 | AuthorizationReq | AuthorizationRes | OK |
| 103 | AuthorizationReq | AuthorizationRes | OK |
| 104 | AuthorizationReq | AuthorizationRes | OK |
| 105 | AuthorizationReq | AuthorizationRes | OK |
| 106 | AuthorizationReq | AuthorizationRes | OK |
| 107 | AuthorizationReq | AuthorizationRes | OK |
| 108 | AuthorizationReq | AuthorizationRes | OK |
| 109 | AuthorizationReq | AuthorizationRes | OK |
| 110 | AuthorizationReq | AuthorizationRes | OK |
| 111 | AuthorizationReq | AuthorizationRes | OK |
| 112 | AuthorizationReq | AuthorizationRes | OK |
| 113 | AuthorizationReq | AuthorizationRes | OK |
| 114 | AuthorizationReq | AuthorizationRes | OK |
| 115 | AuthorizationReq | AuthorizationRes | OK |
| 116 | AuthorizationReq | AuthorizationRes | OK |
| 117 | AuthorizationReq | AuthorizationRes | OK |
| 118 | AuthorizationReq | AuthorizationRes | OK |
| 119 | AuthorizationReq | AuthorizationRes | OK |
| 120 | AuthorizationReq | AuthorizationRes | OK |
| 121 | AuthorizationReq | AuthorizationRes | OK |
| 122 | AuthorizationReq | AuthorizationRes | OK |
| 123 | AuthorizationReq | AuthorizationRes | OK |
| 124 | AuthorizationReq | AuthorizationRes | OK |
| 125 | AuthorizationReq | AuthorizationRes | OK |
| 126 | AuthorizationReq | AuthorizationRes | OK |
| 127 | AuthorizationReq | AuthorizationRes | OK |
| 128 | AuthorizationReq | AuthorizationRes | OK |
| 129 | AuthorizationReq | AuthorizationRes | OK |
| 130 | AuthorizationReq | AuthorizationRes | OK |
| 131 | AuthorizationReq | AuthorizationRes | OK |
| 132 | AuthorizationReq | AuthorizationRes | OK |
| 133 | AuthorizationReq | AuthorizationRes | OK |
| 134 | AuthorizationReq | AuthorizationRes | OK |
| 135 | AuthorizationReq | AuthorizationRes | OK |
| 136 | AuthorizationReq | AuthorizationRes | OK |
| 137 | AuthorizationReq | AuthorizationRes | OK |
| 138 | AuthorizationReq | AuthorizationRes | OK |
| 139 | AuthorizationReq | AuthorizationRes | OK |
| 140 | AuthorizationReq | AuthorizationRes | OK |
| 141 | AuthorizationReq | AuthorizationRes | OK |
| 142 | AuthorizationReq | AuthorizationRes | OK |
| 143 | AuthorizationReq | AuthorizationRes | OK |
| 144 | AuthorizationReq | AuthorizationRes | OK |
| 145 | AuthorizationReq | AuthorizationRes | OK |
| 146 | AuthorizationReq | AuthorizationRes | OK |
| 147 | AuthorizationReq | AuthorizationRes | OK |
| 148 | AuthorizationReq | AuthorizationRes | OK |
| 149 | AuthorizationReq | AuthorizationRes | OK |
| 150 | AuthorizationReq | AuthorizationRes | OK |
| 151 | AuthorizationReq | AuthorizationRes | OK |
| 152 | AuthorizationReq | AuthorizationRes | OK |
| 153 | AuthorizationReq | AuthorizationRes | OK |
| 154 | AuthorizationReq | AuthorizationRes | OK |
| 155 | AuthorizationReq | AuthorizationRes | OK |
| 156 | AuthorizationReq | AuthorizationRes | OK |
| 157 | AuthorizationReq | AuthorizationRes | OK |
| 158 | AuthorizationReq | AuthorizationRes | OK |
| 159 | AuthorizationReq | AuthorizationRes | OK |
| 160 | AuthorizationReq | AuthorizationRes | OK |
| 161 | AuthorizationReq | AuthorizationRes | OK |
| 162 | AuthorizationReq | AuthorizationRes | OK |
| 163 | AuthorizationReq | AuthorizationRes | OK |
| 164 | AuthorizationReq | AuthorizationRes | OK |
| 165 | AuthorizationReq | AuthorizationRes | OK |
| 166 | AuthorizationReq | AuthorizationRes | OK |
| 167 | AuthorizationReq | AuthorizationRes | OK |
| 168 | AuthorizationReq | AuthorizationRes | OK |
| 169 | AuthorizationReq | AuthorizationRes | OK |
| 170 | AuthorizationReq | AuthorizationRes | OK |
| 171 | AuthorizationReq | AuthorizationRes | OK |
| 172 | AuthorizationReq | AuthorizationRes | OK |
| 173 | AuthorizationReq | AuthorizationRes | OK |
| 174 | AuthorizationReq | AuthorizationRes | OK |
| 175 | AuthorizationReq | AuthorizationRes | OK |
| 176 | AuthorizationReq | AuthorizationRes | OK |
| 177 | AuthorizationReq | AuthorizationRes | OK |
| 178 | AuthorizationReq | AuthorizationRes | OK |
| 179 | AuthorizationReq | AuthorizationRes | OK |
| 180 | AuthorizationReq | AuthorizationRes | OK |
| 181 | AuthorizationReq | AuthorizationRes | OK |
| 182 | AuthorizationReq | AuthorizationRes | OK |
| 183 | AuthorizationReq | AuthorizationRes | OK |
| 184 | AuthorizationReq | AuthorizationRes | OK |
| 185 | AuthorizationReq | AuthorizationRes | OK |
| 186 | AuthorizationReq | AuthorizationRes | OK |
| 187 | AuthorizationReq | AuthorizationRes | OK |
| 188 | AuthorizationReq | AuthorizationRes | OK |
| 189 | AuthorizationReq | AuthorizationRes | OK |
| 190 | AuthorizationReq | AuthorizationRes | OK |
| 191 | AuthorizationReq | AuthorizationRes | OK |
| 192 | AuthorizationReq | AuthorizationRes | OK |
| 193 | AuthorizationReq | AuthorizationRes | OK |
| 194 | AuthorizationReq | AuthorizationRes | OK |
| 195 | AuthorizationReq | AuthorizationRes | OK |
| 196 | AuthorizationReq | AuthorizationRes | OK |
| 197 | AuthorizationReq | AuthorizationRes | OK |
| 198 | AuthorizationReq | AuthorizationRes | OK |
| 199 | AuthorizationReq | AuthorizationRes | OK |
| 200 | AuthorizationReq | AuthorizationRes | OK |
| 201 | AuthorizationReq | AuthorizationRes | OK |
| 202 | AuthorizationReq | AuthorizationRes | OK |
| 203 | AuthorizationReq | AuthorizationRes | OK |
| 204 | AuthorizationReq | AuthorizationRes | OK |
| 205 | AuthorizationReq | AuthorizationRes | OK |
| 206 | AuthorizationReq | AuthorizationRes | OK |
| 207 | AuthorizationReq | AuthorizationRes | OK |
| 208 | AuthorizationReq | AuthorizationRes | OK |
| 209 | AuthorizationReq | AuthorizationRes | OK |
| 210 | AuthorizationReq | AuthorizationRes | OK |
| 211 | AuthorizationReq | AuthorizationRes | OK |
| 212 | AuthorizationReq | AuthorizationRes | OK |
| 213 | AuthorizationReq | AuthorizationRes | OK |
| 214 | AuthorizationReq | AuthorizationRes | OK |
| 215 | AuthorizationReq | AuthorizationRes | OK |
| 216 | AuthorizationReq | AuthorizationRes | OK |
| 217 | AuthorizationReq | AuthorizationRes | OK |
| 218 | AuthorizationReq | AuthorizationRes | OK |
| 219 | AuthorizationReq | AuthorizationRes | OK |
| 220 | AuthorizationReq | AuthorizationRes | OK |
| 221 | AuthorizationReq | AuthorizationRes | OK |
| 222 | AuthorizationReq | AuthorizationRes | OK |
| 223 | AuthorizationReq | AuthorizationRes | OK |
| 224 | AuthorizationReq | AuthorizationRes | OK |
| 225 | AuthorizationReq | AuthorizationRes | OK |
| 226 | AuthorizationReq | AuthorizationRes | OK |
| 227 | AuthorizationReq | AuthorizationRes | OK |
| 228 | AuthorizationReq | AuthorizationRes | OK |
| 229 | AuthorizationReq | AuthorizationRes | OK |
| 230 | AuthorizationReq | AuthorizationRes | OK |
| 231 | AuthorizationReq | AuthorizationRes | OK |
| 232 | AuthorizationReq | AuthorizationRes | OK |
| 233 | AuthorizationReq | AuthorizationRes | OK |
| 234 | AuthorizationReq | AuthorizationRes | OK |
| 235 | AuthorizationReq | AuthorizationRes | OK |
| 236 | AuthorizationReq | AuthorizationRes | OK |
| 237 | AuthorizationReq | AuthorizationRes | OK |
| 238 | AuthorizationReq | AuthorizationRes | OK |
| 239 | AuthorizationReq | AuthorizationRes | OK |
| 240 | AuthorizationReq | AuthorizationRes | OK |
| 241 | AuthorizationReq | AuthorizationRes | OK |
| 242 | AuthorizationReq | AuthorizationRes | OK |
| 243 | AuthorizationReq | AuthorizationRes | OK |
| 244 | AuthorizationReq | AuthorizationRes | OK |
| 245 | AuthorizationReq | AuthorizationRes | OK |
| 246 | AuthorizationReq | AuthorizationRes | OK |
| 247 | AuthorizationReq | AuthorizationRes | OK |
| 248 | AuthorizationReq | AuthorizationRes | OK |
| 249 | AuthorizationReq | AuthorizationRes | OK |
| 250 | AuthorizationReq | AuthorizationRes | OK |
| 251 | AuthorizationReq | AuthorizationRes | OK |
| 252 | AuthorizationReq | AuthorizationRes | OK |
| 253 | AuthorizationReq | AuthorizationRes | OK |
| 254 | AuthorizationReq | AuthorizationRes | OK |
| 255 | AuthorizationReq | AuthorizationRes | OK |
| 256 | AuthorizationReq | AuthorizationRes | OK |
| 257 | AuthorizationReq | AuthorizationRes | OK |
| 258 | AuthorizationReq | AuthorizationRes | OK |
| 259 | AuthorizationReq | AuthorizationRes | OK |
| 260 | AuthorizationReq | AuthorizationRes | OK |
| 261 | AuthorizationReq | AuthorizationRes | OK |
| 262 | AuthorizationReq | AuthorizationRes | OK |
| 263 | AuthorizationReq | AuthorizationRes | OK |
| 264 | AuthorizationReq | AuthorizationRes | OK |
| 265 | AuthorizationReq | AuthorizationRes | OK |
| 266 | AuthorizationReq | AuthorizationRes | OK |
| 267 | AuthorizationReq | AuthorizationRes | OK |
| 268 | AuthorizationReq | AuthorizationRes | OK |
| 269 | AuthorizationReq | AuthorizationRes | OK |
| 270 | AuthorizationReq | AuthorizationRes | OK |
| 271 | AuthorizationReq | AuthorizationRes | OK |
| 272 | AuthorizationReq | AuthorizationRes | OK |
| 273 | AuthorizationReq | AuthorizationRes | OK |
| 274 | AuthorizationReq | AuthorizationRes | OK |
| 275 | AuthorizationReq | AuthorizationRes | OK |
| 276 | AuthorizationReq | AuthorizationRes | OK |
| 277 | AuthorizationReq | AuthorizationRes | OK |
| 278 | AuthorizationReq | AuthorizationRes | OK |
| 279 | AuthorizationReq | AuthorizationRes | OK |
| 280 | AuthorizationReq | AuthorizationRes | OK |
| 281 | AuthorizationReq | AuthorizationRes | OK |
| 282 | AuthorizationReq | AuthorizationRes | OK |
| 283 | AuthorizationReq | AuthorizationRes | OK |
| 284 | AuthorizationReq | AuthorizationRes | OK |
| 285 | AuthorizationReq | AuthorizationRes | OK |
| 286 | AuthorizationReq | AuthorizationRes | OK |
| 287 | AuthorizationReq | AuthorizationRes | OK |
| 288 | AuthorizationReq | AuthorizationRes | OK |
| 289 | AuthorizationReq | AuthorizationRes | OK |
| 290 | AuthorizationReq | AuthorizationRes | OK |
| 291 | AuthorizationReq | AuthorizationRes | OK |
| 292 | AuthorizationReq | AuthorizationRes | OK |
| 293 | AuthorizationReq | AuthorizationRes | OK |
| 294 | AuthorizationReq | AuthorizationRes | OK |
| 295 | AuthorizationReq | AuthorizationRes | OK |
| 296 | AuthorizationReq | AuthorizationRes | OK |
| 297 | AuthorizationReq | AuthorizationRes | OK |
| 298 | AuthorizationReq | AuthorizationRes | OK |
| 299 | AuthorizationReq | AuthorizationRes | OK |
| 300 | AuthorizationReq | AuthorizationRes | OK |
| 301 | AuthorizationReq | AuthorizationRes | OK |
| 302 | AuthorizationReq | AuthorizationRes | OK |
| 303 | AuthorizationReq | AuthorizationRes | OK |
| 304 | AuthorizationReq | AuthorizationRes | OK |
| 305 | AuthorizationReq | AuthorizationRes | OK |
| 306 | AuthorizationReq | AuthorizationRes | OK |
| 307 | AuthorizationReq | AuthorizationRes | OK |
| 308 | AuthorizationReq | AuthorizationRes | OK |
| 309 | AuthorizationReq | AuthorizationRes | OK |
| 310 | AuthorizationReq | AuthorizationRes | OK |
| 311 | AuthorizationReq | AuthorizationRes | OK |
| 312 | AuthorizationReq | AuthorizationRes | OK |
| 313 | AuthorizationReq | AuthorizationRes | OK |
| 314 | AuthorizationReq | AuthorizationRes | OK |
| 315 | AuthorizationReq | AuthorizationRes | OK |
| 316 | AuthorizationReq | AuthorizationRes | OK |
| 317 | AuthorizationReq | AuthorizationRes | OK |
| 318 | AuthorizationReq | AuthorizationRes | OK |
| 319 | AuthorizationReq | AuthorizationRes | OK |
| 320 | AuthorizationReq | AuthorizationRes | OK |
| 321 | AuthorizationReq | AuthorizationRes | OK |
| 322 | AuthorizationReq | AuthorizationRes | OK |
| 323 | AuthorizationReq | AuthorizationRes | OK |
| 324 | AuthorizationReq | AuthorizationRes | OK |
| 325 | AuthorizationReq | AuthorizationRes | OK |
| 326 | AuthorizationReq | AuthorizationRes | OK |
| 327 | AuthorizationReq | AuthorizationRes | OK |
| 328 | AuthorizationReq | AuthorizationRes | OK |
| 329 | AuthorizationReq | AuthorizationRes | OK |
| 330 | AuthorizationReq | AuthorizationRes | OK |
| 331 | AuthorizationReq | AuthorizationRes | OK |
| 332 | AuthorizationReq | AuthorizationRes | OK |
| 333 | AuthorizationReq | AuthorizationRes | OK |
| 334 | AuthorizationReq | AuthorizationRes | OK |
| 335 | AuthorizationReq | AuthorizationRes | OK |
| 336 | AuthorizationReq | AuthorizationRes | OK |
| 337 | AuthorizationReq | AuthorizationRes | OK |
| 338 | AuthorizationReq | AuthorizationRes | OK |
| 339 | AuthorizationReq | AuthorizationRes | OK |
| 340 | AuthorizationReq | AuthorizationRes | OK |
| 341 | AuthorizationReq | AuthorizationRes | OK |
| 342 | AuthorizationReq | AuthorizationRes | OK |
| 343 | AuthorizationReq | AuthorizationRes | OK |
| 344 | AuthorizationReq | AuthorizationRes | OK |
| 345 | AuthorizationReq | AuthorizationRes | OK |
| 346 | AuthorizationReq | AuthorizationRes | OK |
| 347 | AuthorizationReq | AuthorizationRes | OK |
| 348 | AuthorizationReq | AuthorizationRes | OK |
| 349 | AuthorizationReq | AuthorizationRes | OK |
| 350 | AuthorizationReq | AuthorizationRes | OK |
| 351 | AuthorizationReq | AuthorizationRes | OK |
| 352 | AuthorizationReq | AuthorizationRes | OK |
| 353 | AuthorizationReq | AuthorizationRes | OK |
| 354 | AuthorizationReq | AuthorizationRes | OK |
| 355 | AuthorizationReq | AuthorizationRes | OK |
| 356 | AuthorizationReq | AuthorizationRes | OK |
| 357 | AuthorizationReq | AuthorizationRes | OK |
| 358 | AuthorizationReq | AuthorizationRes | OK |
| 359 | AuthorizationReq | AuthorizationRes | OK |
| 360 | AuthorizationReq | AuthorizationRes | OK |
| 361 | AuthorizationReq | AuthorizationRes | OK |
| 362 | AuthorizationReq | AuthorizationRes | OK |
| 363 | AuthorizationReq | AuthorizationRes | OK |
| 364 | AuthorizationReq | AuthorizationRes | OK |
| 365 | AuthorizationReq | AuthorizationRes | OK |
| 366 | AuthorizationReq | AuthorizationRes | OK |
| 367 | AuthorizationReq | AuthorizationRes | OK |
| 368 | AuthorizationReq | AuthorizationRes | OK |
| 369 | AuthorizationReq | AuthorizationRes | OK |
| 370 | AuthorizationReq | AuthorizationRes | OK |
| 371 | AuthorizationReq | AuthorizationRes | FAILED |

## Response codes other than OK

- `[371] AuthorizationRes` → **FAILED**

## Against the declared flow — `iso2-ac-pnc (iso15118-2, ac)`

Reference: our own recorded session — the route this stack takes, not a conformance claim.

Consecutive repeats are collapsed on both sides: a session polls, and a compacted
scenario names each request once, so the counts are compared separately from the order.

### EV → station

      SupportedAppProtocolReq
      SessionSetupReq
      ServiceDiscoveryReq
      PaymentServiceSelectionReq
      PaymentDetailsReq
      AuthorizationReq
  -   ChargeParameterDiscoveryReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   ChargingStatusReq   (in the scenario, never on the wire)
  -   MeteringReceiptReq   (in the scenario, never on the wire)
  -   ChargingStatusReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   SessionStopReq   (in the scenario, never on the wire)

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
      ServiceDiscoveryRes
      PaymentServiceSelectionRes
      PaymentDetailsRes
      AuthorizationRes
  -   ChargeParameterDiscoveryRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   ChargingStatusRes   (in the reference, never answered)
  -   MeteringReceiptRes   (in the reference, never answered)
  -   ChargingStatusRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   SessionStopRes   (in the reference, never answered)

Repeat counts (a difference here is usually their compaction, not a defect):

- AuthorizationReq: 367× on the wire, 1× in the scenario

**14 divergence(s) in the order.** Each one is a question for the write-up: our state machine, their capture, or a real disagreement?
