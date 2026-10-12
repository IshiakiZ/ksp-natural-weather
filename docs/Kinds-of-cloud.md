# Kinds of cloud: why, where and how each forms

Asked for on 2026-10-11: "research a whole bunch of different types of clouds: why they form, where they form, and how
they form, so that we can have not just one type of cloud or a couple types of clouds, but a bunch more." And, of the
clouds as the mod drew them that day: "usually the clouds aren't huge bulbs like they are now. That happens maybe every
now and then, but real clouds aren't like that. They're kind of sprawled out."

This page is that research, written for building from: for each kind, what it looks like, what makes it, where and
when it is found, how big it is, what weather it brings, and what the mod has or lacks to draw it. The last two
sections say what follows for the mod. (What real clouds do in general, how the best games draw them, and what the mod
draws now are in [Rework](Rework.md) and [Clouds](Clouds.md); this page does not repeat them.)

## 1. What makes any cloud

A cloud is air that has been brought to the point where it can hold no more water as vapour. There are four ways to
bring it there, and every kind of cloud is one of them, or two together:

| Way | What happens | Clouds it makes |
| --- | --- | --- |
| **Lifted** | air that rises comes under less pressure, swells and cools (9.8 K a kilometre while dry, 4 to 7 once cloud is forming in it) | nearly all cloud: see what lifts it, below |
| **Cooled where it lies** | the ground under it loses heat to a clear night sky, or it drifts over colder ground or water | fog, and stratus when the fog lifts |
| **Given more water** | cold air over warm water; rain falling through dry air and evaporating | steam fog (sea smoke); the ragged scud under a rain cloud |
| **Mixed** | two lots of nearly saturated air at different temperatures, mixed, can be past saturation | a jet's condensation trail, breath on a cold day, a rocket's exhaust |

What lifts air, and the shape of what comes of it:

* **Bubbles from a warmed surface** (convection). Separate lumps of air rising: separate heaps, flat underneath at the
  one height where rising air first condenses, lumpy on top. Cumulus, and grown to the limit, cumulonimbus.
* **A slope of colder air** (a front) or the slow rise of air over hundreds of kilometres round a low. Everything
  rises together, a few centimetres a second: featureless sheets, thickening. Cirrostratus, altostratus, nimbostratus.
* **High ground.** Air forced up a hill: a cap on the top, and downwind a train of standing waves with a smooth lens
  of cloud in each crest.
* **Stirring in a layer.** A layer of damp air stirred by the wind or by its own top cooling to space: a sheet of
  rolls or pads. Stratocumulus low down, altocumulus in the middle levels, cirrocumulus high up.
* **Air running together** (the sea breeze meeting the land's air, the trade winds meeting at the equator, the cold
  outflow of a storm): a line of heaps along where it meets.

Two things then decide what the cloud looks like, more than anything else:

1. **Whether the air it forms in is stable.** If rising air stays warmer than what is round it, it goes on rising
   (unstable: heaps with hard, rounded tops). If it at once becomes cooler than its surroundings, it sinks back
   (stable: flat sheets, smooth edges). A stable layer that is stirred gives the kind between: a sheet broken into
   cells.
2. **Where the lid is.** Nearly every day there is a layer a kilometre or two up where the air gets warmer with
   height instead of colder (an inversion: the top of the air that the ground has stirred that day, or sinking air
   under a high). Rising air stops there as at a ceiling. What cloud there is lies between the height where it
   condenses and the lid, which is often only a few hundred metres, and spreads out under the lid.

Whether it is water or ice decides its edges: water cloud has sharp edges (droplets evaporate in seconds once they
leave the cloud); ice cloud is fibrous and silky, drawn out in streaks (crystals last many minutes in clear air and
fall as they go).

## 2. Why the usual sky is spread out and not heaped

The user is right, and the numbers say so.

* **The commonest cloud on Earth is a flat one.** Stratocumulus, a low lumpy sheet, covers about 23 % of the oceans
  and 12 % of the land averaged over the year, more of the planet than any other kind, and over the cool eastern
  sides of the oceans more than half the time ([Wood, 2012](https://atmos.washington.edu/~robwood/papers/reviews/sc_review_revised_single.pdf);
  the figures as given in [ACP 22, 12241](https://acp.copernicus.org/articles/22/12241/2022/acp-22-12241-2022.html)).
* **Most heaps are wider than they are tall.** The cloud atlas names fair-weather heaps *humilis*: flattened, wider
  than tall, depths that hardly vary. *Mediocris* are about as tall as wide. Only *congestus* are taller than wide,
  the cauliflower tower, and they are the sign of deep unstable air: a day of showers
  ([cumulus species](https://www.whatsthiscloud.com/cloud-species/mediocris)).
* **Why.** On a fair day rising air condenses 600 to 1,500 m up and meets the lid at one to two kilometres. The
  cloud can be no deeper than the gap, a few hundred metres, while the bubbles that feed it are some hundreds of
  metres to a kilometre or two across. So: wide, shallow, flat on both faces.
* **And then it spreads.** A heap whose top reaches the lid cannot go up, so it goes sideways: the top widens into
  a patch of stratocumulus that outlives the heap under it (*cumulogenitus*), the heap often fading from its base
  upward and leaving the patch. Where the wind is stronger at the lid the tops are carried forward as they spread.
  In the evening, when the ground stops feeding them, the domes flatten and what is left of a sky of heaps is a
  sky of flat, ragged pads ([WMO: how stratocumulus forms](https://cloudatlas.wmo.int/en/stratocumulus-may-form.html)).
* **A big bulb is a particular day.** It needs deep, damp, unstable air and no lid: showery weather behind a cold
  front, the tropics, a summer afternoon that will end in thunder.

What the mod drew on 2026-10-11 as a fair day was heaps 1.3 to 1.8 km deep over bases at 1 km (`CloudsStandIn.cs`,
"fair"): every fair day was the showery day.

## 3. The ten kinds

The World Meteorological Organization's [International Cloud Atlas](https://cloudatlas.wmo.int/en/cloud-classification-summary.html)
sorts all cloud into ten kinds (genera) by height and shape, each with named forms (species), arrangements
(varieties) and features. Heights are for middle latitudes on Earth; Kerbin's air thins faster, and the mod puts
everything a little lower ([Clouds](Clouds.md)).

### Low: bases below 2 km

**Cumulus** (heaps).
*Looks:* separate clouds with flat grey bases all at one height and white rounded tops, sharp-edged; blue between.
*Forms:* bubbles of warmed air from the ground rise past the height where they condense. Each turret is one bubble:
hard and bulging while it rises, fraying and thinning when it has stopped, gone in ten to forty minutes while the
next rises beside it. *Where and when:* over land by day wherever the sun warms damp ground: first at mid-morning,
most at mid-afternoon, gone by dusk; over warm sea at any hour (the trade winds' steady small heaps, tops held at
1.5 to 2.5 km by the trade inversion). *Forms it takes:* **humilis** (flat: fair weather), **mediocris** (as tall
as wide, a few sprouts on top), **congestus** (towers to 4 to 6 km: showers), **fractus** (ragged shreds: a heap
beginning or dying, or torn off in wind). **Radiatus**: rows along the wind (cloud streets), when the wind is fresh
and steady: rows two to four times the depth of the stirred layer apart, tens to hundreds of kilometres long.
*With it:* a smooth cap on a fast-growing tower (**pileus**: the damp air above pushed up and condensing for a
minute); rain that does not reach the ground (**virga**).

**Stratocumulus** (the lumpy sheet).
*Looks:* a low grey-and-white sheet or patches made of big rounded pads or rolls, with darker undersides and paler
thin places or gaps of blue between; each element wider than three fingers at arm's length (5 degrees). The usual
"cloudy but dry" sky. *Forms:* (1) a damp layer under the lid, stirred from the top: the cloud top loses heat to
space, the cooled air sinks through the cloud and stirs it into cells; (2) heaps spreading under the lid, as
above; (3) a sheet of stratus lifted and broken up by the day's warming or a rising wind; (4) the tail end of a
rain sheet thinning. *Where and when:* everywhere, and above all over cool sea under sinking air: the great decks
off California, Peru, Namibia and the Canaries, hundreds of kilometres across. Over sea it is thickest before dawn
and thins or breaks in the afternoon; over land it comes in the evening as the heaps flatten. *Size:* 200 to 400 m
thick, top at 0.5 to 2 km. From orbit the decks are honeycombed in cells 10 to 40 km across: **closed cells** (pads
of cloud with clear edges) where it is dry, **open cells** (clear middles ringed with cloud) where it drizzles.
*Forms it takes:* rolls in ranks across the wind (**undulatus**), with gaps (**perlucidus**) or solid (**opacus**),
a single long roll (**volutus**, the roll cloud), small turrets rising from a flat base (**castellanus**).
*Brings:* little: drizzle at most.

**Stratus** (the grey sheet).
*Looks:* a featureless grey layer, low enough to hide hilltops and tall buildings; the sun through it, when it
shows, has a sharp edge. *Forms:* fog that has lifted off the ground (the wind rose, or the sun warmed the ground);
damp air cooled by flowing over cold ground or sea; damp air pushed gently up a slope; and under a rain cloud,
ragged shreds (**fractus**, "scud") where the rain has wetted the air below the base. *Where and when:* coasts and
cold seas, winter highs over land (days of gloom under an inversion), the Arctic in summer. *Size:* tens to a few
hundred metres thick, base from the ground to 600 m. *Brings:* drizzle, or fine snow.

**Cumulonimbus** (the storm).
*Looks:* a mountain of cloud from a dark base a kilometre up to the top of the weather, 8 to 18 km, where it
spreads into a flat anvil of ice. *Forms:* a congestus that keeps rising because the air is unstable all the way
up. One cell lives 30 to 60 minutes: a tower with air rising at 10 to 30 m/s; then rain and hail dragging air down
through it; then the downdraught chokes the updraught and it rains itself out, leaving the anvil adrift as thick
cirrus. Where wind changes with height the downdraught falls beside the updraught and not through it, and the
storm lasts for hours. *Where and when:* over land on summer afternoons and evenings; along cold fronts; over warm
sea at night; in a band round the equator. *With it:* **calvus** (top still rounded: just turning to ice) then
**capillatus** (top fibrous); the anvil (**incus**), tens to hundreds of kilometres across, carried downwind;
pouches hanging under the anvil (**mamma**, each one to three kilometres across); a dark wedge or shelf low on the
leading edge where the cold outflow lifts the warm air ahead (**arcus**); a lowered, turning wall under the
updraught (**murus**) and a funnel (**tuba**) in the worst; rain shafts (**praecipitatio**); scud beneath
(**pannus**). *Brings:* heavy rain, hail, lightning, squalls.

### Middle: bases 2 to 7 km

**Altocumulus** (the dappled or mackerel sky).
*Looks:* a sheet or patches of small white-and-grey rounded masses, rolls or plates in ranks, each one to three
fingers wide at arm's length (1 to 5 degrees), often with blue between; rainbow colours round the sun or moon
through its thin parts. *Forms:* the same stirring-in-a-layer as stratocumulus, higher: a thin damp layer whose top
cools to space breaks into cells a few hundred metres across; also a sheet of altostratus breaking up, the spread
top of a heap that stopped at a lid in the middle levels, or damp air lifted in waves by mountains or ahead of a
front. It is not fully understood ([AMS glossary](https://glossary.ametsoc.org/wiki/Altocumulus)). *Forms it
takes:* **stratiformis** (the wide dappled sheet), **undulatus** (parallel ripples: waves on the layer),
**lenticularis** (smooth lenses standing still downwind of mountains while the wind blows through them, sometimes
stacked like plates), **castellanus** (a row of little turrets on a common base: the middle air is unstable, and
thunder often follows by evening), **floccus** (ragged tufts, each trailing a wisp of falling ice). *Size:* a few
hundred metres thick. *Brings:* nothing itself. A mackerel sky thickening from the west is a front coming.

**Altostratus** (the ground-glass sky).
*Looks:* a grey or bluish even sheet over all the sky; the sun shows through as a bright patch without an edge, as
through ground glass, and casts no shadows; no halo. *Forms:* air climbing the gentle slope of a warm front,
hundreds of kilometres ahead of the rain: cirrostratus that has thickened and lowered. *Size:* one to several
kilometres thick. *Brings:* light rain or snow as it thickens; then it is nimbostratus.

**Nimbostratus** (the rain sheet).
*Looks:* thick, dark, featureless; it blots out the sun; the base is blurred by falling rain and hung with scud.
*Forms:* altostratus thickened and lowered as the front comes on; deep, slow, steady rise of damp air. *Size:* 2 to
4 km thick and more, from below a kilometre up through the middle levels, hundreds of kilometres across. *Brings:*
steady rain or snow for hours.

### High: bases above 5 km, all ice

**Cirrus** (the wisps).
*Looks:* white silky threads, hooks and feathery patches. *Forms:* ice crystals form in small heads where the high
air is rising a little, and fall out of them for kilometres into slower, drier air below, so that the streak is
left behind and bent (**uncinus**, the hook or mare's tail; **fibratus**, straight threads laid along the wind).
Also what is left of a storm's anvil (**spissatus**: dense, grey toward the sun), and aircraft trails that have
spread. *Where and when:* ahead of fronts and along the jet streams, where they race along at 30 to 60 m/s; over
the tropics from storms. *Brings:* nothing; hooks thickening into a veil are the first sign of a front, half a day
to a day ahead.

**Cirrocumulus** (the finest ripples).
*Looks:* a thin white patch of tiny grains or ripples with no shading, each smaller than a finger's width at arm's
length (under 1 degree). *Forms:* a very thin high layer stirred into cells; droplets far below freezing that soon
turn to ice, so it does not last and becomes cirrus or cirrostratus. Uncommon.

**Cirrostratus** (the veil).
*Looks:* a milky veil over the sky, often only known by the ring it makes round the sun or moon (a halo, 22
degrees out); shadows are still cast. *Forms:* the first, highest part of the air climbing a warm front; or cirrus
merging. *Brings:* nothing yet; rain in 12 to 24 hours if it thickens.

## 4. Cloud made by one particular thing

**Fog** (cloud on the ground), by what makes it:
*radiation fog*: a clear calm night, the ground cools, the lowest air with it: forms after midnight, deepest at
dawn, in hollows and valleys first, a few metres to a couple of hundred deep; burnt off from the edges by
mid-morning, the last of it lifting into stratus. *Advection fog*: warm damp air moving over cold sea or cold
ground: banks that roll in from the sea at any hour and can last days. *Upslope fog*: damp air blown up a long
rise. *Steam fog* (sea smoke): very cold air over much warmer water: wisps rising a metre to tens of metres and
vanishing. *Valley fog*: cold air pooled in a valley, seen from above as a lake of cloud with the hills standing
out of it.

**Made by mountains.** A *cap* sitting on a summit; a *banner* streaming from the lee of a sharp peak; *lenses*
(lenticularis) standing in the crests of waves downwind, 5 to 25 km apart, when stable air crosses a ridge at
10 m/s and more; a ragged *rotor* cloud turning under the first wave crest; a *wall* of cloud lying along the crest
with clear air pouring down the lee side (the föhn wall).

**Made by wind against wind.** *Billows* (**fluctus**): a row of curls like breaking waves along a cloud top, where
fast air slides over slow across a stable layer; they last a minute or two. *Roll cloud* (**volutus**): one long
horizontal tube, turning about its length, running ahead of a storm's outflow or a sea-breeze front; northern
Australia's Morning Glory is one a thousand kilometres long. *Shelf cloud* (**arcus**): the same thing still joined
to its storm. *Asperitas*: a cloud base like a rough sea seen from below. *Mamma*: pouches where cold, ice-laden air
sinks out of an anvil.

**Made along a line where air meets.** The sea-breeze front: a line of heaps a few kilometres inland on a summer
afternoon, clear sky over the cooled coast behind it. Islands: a heap over each island by day, and downwind of a
mountainous island in a steady wind a street of alternating whirls in the cloud deck. Cold air blowing off land or
ice onto warmer sea: cloud streets that begin a little way offshore and widen downwind into open cells.

**Made by fire, people and rockets.**
*Flammagenitus* (pyrocumulus): a heap over a big fire or a volcano, grey-brown at the bottom, white where it
condenses on top. *Condensation trails* (**homogenitus**): water from engines freezing in air colder than about
-40 C, so above some 8 km: a line that vanishes in seconds in dry air and in damp air lasts for hours and spreads
into cirrus. *The hole* (**cavum**, the fallstreak hole): something passing through a thin layer of cloud whose
droplets are far below freezing sets them freezing; the ice falls out as a streak of cirrus and leaves a round
hole that widens for an hour, to tens of kilometres. Aircraft do it; so does a rocket. *A rocket's own cloud:* its
exhaust is mostly water and makes a trail wherever the air is cold or damp enough; high up after sunset or before
sunrise, the trail is in sunlight against a dark sky and glows, twisted by the winds at each height.

**Above the weather.** *Nacreous* (mother-of-pearl) cloud, 15 to 25 km, in polar winter, colours like oil on water
after sunset. *Noctilucent* cloud, about 80 km: thin electric-blue ripples seen in deep twilight in high latitudes
in summer, ice on meteor dust; the water from large rockets has made them.

## 5. Where on a world, and when

| Where | What the sky usually holds |
| --- | --- |
| The equator's rain belt | towering heaps and storms by afternoon, their anvils as cirrus above; hazy, damp |
| The trade winds (10 to 25 degrees) | small flat heaps in a steady wind, a third of the sky, tops all at one level; streets |
| The dry belts (25 to 35 degrees) | clear over land (deserts); over the cool eastern sides of oceans, decks of stratocumulus and coastal fog |
| The westerlies (40 to 60 degrees) | everything, in turn, as lows pass: see a front, below |
| High latitudes | thin ice cloud, low stratus and fog in summer, shallow heaps over open water |
| A coast by day | a line of heaps just inland; fog banks where the sea is cold |
| Mountains | the first heaps of the day and the first storms; caps and lenses in wind |
| Over the sea against over the land | sea: sheets, lower bases (500 to 800 m), hardly changing with the hour. Land: heaps that follow the sun, higher bases (1 to 2.5 km in a dry afternoon) |

By the clock, over land in settled weather: clear or fog at dawn; first shreds at mid-morning; flat heaps by noon;
a few taller by mid-afternoon; tops spreading into pads by evening; clear by night, or a broken sheet. Over sea:
the sheet thickest before dawn, thinnest in the afternoon.

As a warm front comes (a day): hooks of cirrus; a veil with a halo; the ground-glass sheet, lowering; steady rain
from a dark sheet with scud under it; then breaks. As a cold front passes (an hour or two): a wall of towering
heaps and storms, a squall, then suddenly clear deep-blue sky with bright hard-edged heaps and showers.

## 6. What the mod has, and what each kind needs

The mod describes the cloud over a place as three layers (low, middle, high), each a kind, a cover, a base and a
top (`Keystone/Weather.cs`, `CloudKind`), and draws heaps from a field of thermals made beforehand, sheets from
layers, and wisps from a pattern ([Rework](Rework.md)).

| Kind | Now | What it needs |
| --- | --- | --- |
| Cumulus humilis and mediocris | drawn as deep towers | **the usual sky: shallow.** Depth from the weather's own lid (the gap between where air condenses and the inversion), most days 150 to 600 m; heaps wider than tall; flat on top as well where they reach the lid |
| Spreading tops (stratocumulus from heaps) | no | where a heap reaches the lid, a thin pad spreading from its top, lasting after the heap; all of them by evening |
| Cumulus fractus | no | ragged shreds: the smallest and the dying |
| Cloud streets | no | heaps set in rows along a fresh steady wind |
| Cumulus congestus | this is what "fair" draws | kept for unstable days only |
| Cumulonimbus | a tower | the anvil spreading downwind and left behind as cirrus; pouches under it; the shelf at its leading edge; rain shafts and scud |
| Stratocumulus | one lumpy layer | pads and rolls with gaps; ranks across the wind; over sea the open and closed cells seen from high up; thickest before dawn |
| Stratus | a sheet | scud under rain; lifted fog |
| Nimbostratus | named, drawn as stratus | the dark base blurred by rain |
| Altocumulus | one patchy layer | the dappled sheet of small pads; ripples; turrets before thunder; tufts trailing ice; lenses near mountains |
| Altostratus | a veil | the sun as a blur without a disc |
| Cirrus | a pattern of wisps | hooks: a head and a fall streak bent by the wind's change with height; straight threads on the jet; an old anvil |
| Cirrocumulus | no | finest ripples, in a patch, seldom |
| Cirrostratus | no | a veil that only the sun's ring shows |
| Fog by kind | one fog | valleys at dawn, burning off; banks from a cold sea; steam on warm water in cold air |
| Mountain cloud | no | a cap, a banner, lenses downwind (Kerbin's ranges west of the space centre) |
| Billows, roll cloud | no | rare decorations: last |
| Condensation trails, a rocket's trail | no | **a rocket's own**: a trail where the air is cold or damp enough; lit after sunset high up |
| The hole a rocket leaves | no | with the user's ask that a rocket stir and curl the cloud it goes through: a hole and a falling streak in a thin cold layer; a stirred, curling wake in a thick one |
| Noctilucent | no | high latitudes, summer twilight; after a launch |

## 7. What to do first

1. **Make the usual sky the usual sky.** Fair weather as shallow, wide heaps under a lid, with spreading tops and
   ragged edges; deep towers only when the weather says the air is unstable and damp. Of all of this it is the
   one thing the user asked for outright.
2. **Stratocumulus as it is** (pads and gaps, ranks, the sea's decks): the commonest cloud, and the mod's weakest
   layer.
3. **The middle sky**: the dappled sheet and its ripples; with cirrus hooks above, most real skies are then within
   reach: heaps below, dapple in the middle, wisps on top.
4. **The storm's parts** (anvil, shelf, rain shafts), since the storm sea and the rain are being worked on too.
5. **A rocket and the cloud**: its trail, the hole and the stirred wake.
6. **Fog by kind, mountain cloud**, then the rare ones.

## Sources

* World Meteorological Organization, [International Cloud Atlas](https://cloudatlas.wmo.int/): the
  [table of kinds, forms and features](https://cloudatlas.wmo.int/en/cloud-classification-summary.html); [how
  stratocumulus forms](https://cloudatlas.wmo.int/en/stratocumulus-may-form.html).
* R. Wood, "Stratocumulus clouds", *Monthly Weather Review* 140 (2012): [the review](https://atmos.washington.edu/~robwood/papers/reviews/sc_review_revised_single.pdf);
  its cover figures as quoted in [Atmos. Chem. Phys. 22, 12241 (2022)](https://acp.copernicus.org/articles/22/12241/2022/acp-22-12241-2022.html).
* University of Washington, [Climatic Atlas of Clouds Over Land and Ocean](https://atmos.washington.edu/CloudMap/introduction.html)
  (Warren, Hahn and others: how often each kind is seen, from fifty years of observers' reports).
* American Meteorological Society, Glossary of Meteorology: [altocumulus](https://glossary.ametsoc.org/wiki/Altocumulus).
* Met Office: [mid-level clouds](https://weather.metoffice.gov.uk/learn-about/weather/types-of-weather/clouds/mid-level-clouds),
  [high clouds](https://weather.metoffice.gov.uk/learn-about/weather/types-of-weather/clouds/high-clouds),
  [lenticular clouds](https://metoffice.gov.uk/learning/clouds/other-clouds/lenticular).
* Wave and roll clouds: [Kelvin-Helmholtz waves](https://skybrary.aero/articles/kelvin-helmholtz-waves),
  [arcus](https://en.wikipedia.org/wiki/Arcus_cloud), [mammatus](https://en.wikipedia.org/wiki/Mammatus_cloud),
  [asperitas](https://skybrary.aero/articles/asperitas).
* The shapes of heaps: [mediocris, between humilis and congestus](https://www.whatsthiscloud.com/cloud-species/mediocris);
  trade-wind heaps seen by radar, [Atmos. Meas. Tech. (2022)](https://doaj.org/article/dd953621e5a04c9c9e2416329bc673a4).

Not checked against a source here, and to be checked before any number is built on: the sizes given for the cells
of stratocumulus decks, the spacing of cloud streets, the heights of nacreous and noctilucent cloud, and the
temperature at which trails form. They are the textbook figures as remembered.
