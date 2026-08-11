// What 32 bits of seed costs a 128-bit value: draw N Plug & Charge GenChallenges the way
// EVerest's libiso15118 draws them, and count how many come out twice.
//
// Their AuthorizationSetup::handle_request (src/iso15118/d20/state/authorization_setup.cpp)
// fills the 16-byte challenge with the five lines reproduced below — a fresh std::random_device
// and a fresh std::mt19937 per challenge, seeded with one 32-bit draw. The array is 128 bits
// wide and can take at most 2^32 values, so by the birthday bound a repeat is expected after
// roughly 2^16 challenges rather than the 2^60 a 120-bit-entropy value would give.
//
// The control draws the same number of 16-byte values straight from the kernel CSPRNG. It is
// there so the number below is a statement about the generator and not about the counting.
//
//     g++ -O3 -march=native -o collide collide.cpp
//     ./collide 262144
//
// This uses the real std::random_device, so the count varies run to run around its expectation
// (N^2 / 2^33). That is the point: it is a measurement, not a fixed vector.

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <random>
#include <string>
#include <unordered_map>
#include <vector>

namespace {

constexpr std::size_t GEN_CHALLENGE_LENGTH = 16; // their common_types.hpp

std::string hex(const std::vector<std::uint8_t>& bytes) {
    std::string out;
    char buf[3];
    for (auto byte : bytes) {
        std::snprintf(buf, sizeof(buf), "%02x", byte);
        out += buf;
    }
    return out;
}

// Their five lines, unchanged.
std::vector<std::uint8_t> their_gen_challenge() {
    std::random_device rd;
    std::mt19937 generator(rd());
    std::uniform_int_distribution<std::uint8_t> distribution(0x00, 0xff);

    std::array<std::uint8_t, GEN_CHALLENGE_LENGTH> gen_challenge{};
    for (auto& item : gen_challenge) {
        item = distribution(generator);
    }
    return {gen_challenge.begin(), gen_challenge.end()};
}

std::size_t count_repeats(const char* label, std::size_t n, std::vector<std::uint8_t> (*draw)(),
                          bool show) {
    std::unordered_map<std::string, std::size_t> seen;
    std::size_t repeats = 0;
    for (std::size_t i = 0; i < n; ++i) {
        const auto value = hex(draw());
        const auto found = seen.find(value);
        if (found != seen.end()) {
            ++repeats;
            if (show && repeats <= 5) {
                std::printf("    draw %zu repeats draw %zu: %s\n", i, found->second, value.c_str());
            }
        } else {
            seen.emplace(value, i);
        }
    }
    std::printf("  %-28s %zu draws, %zu distinct, %zu repeated\n", label, n, seen.size(), repeats);
    return repeats;
}

std::vector<std::uint8_t> urandom_challenge() {
    static std::ifstream source("/dev/urandom", std::ios::binary);
    std::vector<std::uint8_t> out(GEN_CHALLENGE_LENGTH);
    source.read(reinterpret_cast<char*>(out.data()), static_cast<std::streamsize>(out.size()));
    return out;
}

} // namespace

int main(int argc, char** argv) {
    const std::size_t n = (argc > 1) ? std::strtoull(argv[1], nullptr, 10) : 262144;

    std::printf("drawing %zu 16-byte GenChallenges each way\n", n);
    std::printf("expected repeats for a 32-bit value space: %.1f\n",
                static_cast<double>(n) * static_cast<double>(n) / 2.0 / 4294967296.0);
    std::printf("expected repeats for a 128-bit value space: ~0\n\n");

    const auto theirs = count_repeats("libiso15118 (mt19937):", n, their_gen_challenge, true);
    const auto control = count_repeats("control (/dev/urandom):", n, urandom_challenge, true);

    std::printf("\n%s\n", theirs > control ? "the 16-byte challenge repeats; the 16-byte control does not"
                                           : "no difference measured at this N -- raise it");
    return 0;
}
