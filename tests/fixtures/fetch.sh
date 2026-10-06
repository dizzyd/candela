#!/usr/bin/env bash
# Downloads the third-party mods the compatibility tests run against into tests/fixtures/Mods.
# They are other authors' work, so they are fetched rather than kept in this repository.
set -euo pipefail

dir="$(cd "$(dirname "$0")" && pwd)/Mods"
mkdir -p "$dir"

fetch() {
    [ -f "$dir/$1" ] && { echo "have  $1"; return; }
    echo "fetch $1"
    curl -fsSL -o "$dir/$1.part" "$2"
    echo "$3  $dir/$1.part" | sha256sum -c --quiet
    mv "$dir/$1.part" "$dir/$1"
}

# Mods from a player's server whose firepits opened their dialog instead of dipping
# (CompatServerMods).
fetch thebasics_5_10_0_pre_1.zip \
    "https://moddbcdn.vintagestory.at/thebasics_5_10_0_pre_42e044f1c2de1c0945822c1cee9a22dd.zip?dl=thebasics_5_10_0_pre_1.zip" \
    ff1a4b893a9da9fd7253925893aae64e4901a62c8c86f8f5bd3fec825a59ee13
fetch NewfiesServerEssentialsSuiteV1.2.37.zip \
    "https://moddbcdn.vintagestory.at/NewfiesServerEssenti_e61257e78d39150e57108f523e4698af.zip?dl=NewfiesServerEssentialsSuiteV1.2.37.zip" \
    b28d6750641c08586f8cf60c40375b58014a90ad1bddb8757416c8763539fb5e
fetch NewfiesAntiCheatClientV1.1.27.zip \
    "https://moddbcdn.vintagestory.at/NewfiesAntiCheatClie_a9291f8ca3e78cfd6316ad5ecb6635e7.zip?dl=NewfiesAntiCheatClientV1.1.27.zip" \
    8c887c0aeeb7ba9a4edf78d8ae71b0a23d89120f62eb65476be2b3f6dfff23e8
fetch vsfirewood-0.4.2.zip \
    "https://moddbcdn.vintagestory.at/vsfirewood-0.4.2_77b56a4f36b36229470ccc53170ba4d9.zip?dl=vsfirewood-0.4.2.zip" \
    4f5f01819f9d599bfd2f8ade2160e1c41e84e63351bb04deebb6b9f28a540d83

# Art of Growing builds its own firepit from dry grass, which the dip vat once missed
# (CompatArtOfGrowing). Core of Arts is a dependency of it.
fetch CoreOfArts_1.2.2.zip \
    "https://moddbcdn.vintagestory.at/CoreOfArts_1.2.2_c618a8ecf47f5e02acf3eed7386d341a.zip?dl=CoreOfArts_1.2.2.zip" \
    937800ec4d400bffdf72bf8c5b429505beaf55004b89c72663c03e1d0862a78a
fetch ArtOfGrowing_1.2.2.zip \
    "https://moddbcdn.vintagestory.at/ArtOfGrowing_1.2.2_36314fcae3ae4e2cd1511c5edab64c95.zip?dl=ArtOfGrowing_1.2.2.zip" \
    39f08f333b98c0d561f1e3207a7a7d56a154acc8ce0719378af324a68acddf76
