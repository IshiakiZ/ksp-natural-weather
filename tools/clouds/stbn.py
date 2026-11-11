#!/usr/bin/env python3
"""stbn.py: the spatiotemporal blue noise the clouds' far walk takes its steps' places from (Clouds.cs LoadBlue, clouds.glsl blueAt).
After Wolfe, Morrical, Akenine-Moeller and Ramamoorthi 2022 (arXiv 2112.09629), in our own words: void and cluster (Ulichney 1993)
over 64 x 64 places and 64 frames, the energy at each place the sum of a Gaussian over the other places of its own frame and one
over its own place's other frames (sigma 1.9, wrapping), so that each frame's square is blue and so is each place's run of values
over the frames. Written as 64 squares of 64 x 64, eight across and eight up, one byte a place, each square's 4096 places ranked
within it (16 to each value).
    python3 tools/clouds/stbn.py src/NaturalWeather/PluginData/blue.stbn
"""
import sys
import numpy as np

N, T, SIGMA = 64, 64, 1.9


def ring(n):
    d = np.minimum(np.arange(n), n - np.arange(n)).astype(float)
    return np.exp(-d * d / (2 * SIGMA * SIGMA))


SPACE, TIME = np.outer(ring(N), ring(N)), ring(T)


def put(energy, t, y, x, sign):
    energy[t] += sign * np.roll(np.roll(SPACE, y, 0), x, 1)
    energy[:, y, x] += sign * np.roll(TIME, t)


def tightest(on, energy):
    return np.unravel_index(np.argmax(np.where(on, energy, -np.inf)), on.shape)


def emptiest(on, energy):
    return np.unravel_index(np.argmin(np.where(on, np.inf, energy)), on.shape)


def main(out):
    rng = np.random.default_rng(20261026)
    on = rng.random((T, N, N)) < 0.1
    energy = np.zeros(on.shape)
    for t, y, x in zip(*np.nonzero(on)):
        put(energy, t, y, x, 1.0)
    while True:  # the first points spread: the tightest cluster's point to the emptiest place, until it is the same place
        c = tightest(on, energy); on[c] = False; put(energy, *c, -1.0)
        v = emptiest(on, energy); on[v] = True; put(energy, *v, 1.0)
        if c == v:
            break
    rank = np.zeros(on.shape, np.int64)
    first, on0, energy0 = int(on.sum()), on.copy(), energy.copy()
    for r in range(first - 1, -1, -1):  # below the first points: each time the tightest cluster's taken away
        c = tightest(on, energy); on[c] = False; put(energy, *c, -1.0); rank[c] = r
    on, energy = on0, energy0
    for r in range(first, on.size):  # above: each time the emptiest place filled
        v = emptiest(on, energy); on[v] = True; put(energy, *v, 1.0); rank[v] = r
    atlas = np.zeros((8 * N, 8 * N), np.uint8)
    for t in range(T):
        order = np.argsort(rank[t].ravel(), kind="stable")
        value = np.empty(N * N, np.int64); value[order] = np.arange(N * N) * 256 // (N * N)
        y0, x0 = (t >> 3) * N, (t & 7) * N
        atlas[y0:y0 + N, x0:x0 + N] = value.reshape(N, N)
    # (each square holds 16 of each value)
    assert all((np.bincount(atlas[(t >> 3) * N:(t >> 3) * N + N, (t & 7) * N:(t & 7) * N + N].ravel(), minlength=256) == 16).all() for t in range(T))
    atlas.tofile(out)


if __name__ == "__main__":
    main(sys.argv[1])
