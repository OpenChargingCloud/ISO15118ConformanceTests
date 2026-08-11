/* Minimal isolation of the crash in EVerest EvseV2G's handle_iso_payment_details.
 *
 * handle_iso_payment_details (modules/EVSE/EvseV2G/iso_server.cpp) parses the EV's
 * contract certificate, then calls getEmaidFromContractCert(contract_crt) at line
 * 990 -- BEFORE it checks the parse result `err` at line 1006. When the EXI carried
 * a non-empty but unparseable ContractSignatureCertChain.Certificate, der_to_certificate
 * returns a null certificate_ptr, so contract_crt.get() is NULL and
 * getEmaidFromContractCert -> certificate_subject(NULL) runs on a null X509*.
 *
 * certificate_subject (lib/everest/tls/src/openssl_util.cpp:774) opens with
 * `assert(cert != nullptr)` and then calls X509_get_subject_name(cert). This program
 * reproduces the two outcomes:
 *   - debug build (assert live):   SIGABRT at the assert
 *   - release build (-DNDEBUG):    SIGSEGV in X509_get_subject_name(NULL), shown here
 *
 *   cc -O2 -DNDEBUG nullderef.c -o nullderef -lcrypto && ./nullderef   # -> SIGSEGV
 *   cc -O2         nullderef.c -o nullderef -lcrypto && ./nullderef   # -> SIGABRT via assert()
 */
#include <openssl/x509.h>
#include <assert.h>
#include <stdio.h>

/* certificate_subject's first two lines, verbatim in spirit */
static void certificate_subject_head(const X509* cert) {
    assert(cert != NULL);                       /* debug: aborts here */
    (void) X509_get_subject_name(cert);         /* release: dereferences NULL here */
}

int main(void) {
    X509* contract_crt = NULL;                  /* what der_to_certificate leaves on bad DER */
    printf("calling certificate_subject(NULL) the way line 990 does...\n");
    fflush(stdout);
    certificate_subject_head(contract_crt);
    printf("survived -- not the build under test\n");
    return 0;
}
