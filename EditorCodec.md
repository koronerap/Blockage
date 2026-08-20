# EditorCodec — Mimicraft dosya formatları

Bu belge, **başka bir araçta yapılmış voxel modelleri Mimicraft'a sokabilmek** için gereken ikili
formatları tanımlar. Kaynak: `Assets/Scripts/Networking/VoxelBodyCodec.cs`,
`CharacterCodec.cs`, `Assets/Scripts/Customization/CharacterFile.cs`, `WeaponSkinFile.cs`.

İki tarafın da anlaşması gereken tek şey bayt düzeni. Aşağıdakini üreten herhangi bir dil/araç,
Mimicraft'ın okuyabileceği bir dosya çıkarır.

---

## 0. Ne üretmek istiyorsun?

| Amaç | Dosya | Bölüm |
|---|---|---|
| Bir karakter (vücut parçaları) | `.character` | [4](#4-character--bir-karakter-dosyası) |
| Bir silah modeli | `.weapons` | [5](#5-weapons--bir-silah-dosyası) |
| Modelci gövdesi / ham voxel gövdesi | (ağ payload'ı) | [3](#3-voxelbodycodec--gövde-payloadı) |

Üçü de aynı çekirdeği kullanır: **VoxelBodyCodec payload'ı**. Karakter ve silah formatları onun
üstüne yalnızca "hangi parça hangi slot" bilgisini ekler.

Dosyaları Unity'ye atmak için `Assets/` altına kopyala — projede her iki uzantı için de bir
`ScriptedImporter` var, asset olarak içe aktarılırlar.

---

## 1. Ortak ilkeller

Tüm sayısal alanlar **little-endian**.

### varint (LEB128, işaretsiz)

7 bit veri + en yüksek bit "devam" bayrağı. `uint32` için en fazla 5 bayt.

```python
def write_varint(out, value):
    while value >= 0x80:
        out.append((value | 0x80) & 0xFF)
        value >>= 7
    out.append(value & 0xFF)
```

Okuyucu 5 baytta durur; daha uzun bir dizi **geçersizdir**, tamponu taramaz.

### float

IEEE-754 32-bit, little-endian (`BitConverter.GetBytes(float)`).

### Uzunluk önekli string

.NET `BinaryWriter.Write(string)` biçimi: **7-bit kodlanmış uzunluk** (varint ile aynı düzen,
bayt cinsinden UTF-8 uzunluğu), ardından UTF-8 baytları.

```python
def write_string(out, s):
    data = s.encode("utf-8")
    write_varint(out, len(data))     # aynı 7-bit düzen
    out += data
```

### Renk

`r`, `g`, `b` — üç bayt. **Alpha yok**; voxel renkleri opak.

---

## 2. Voxel yerleşimi ve indeksleme

Bir **parça** (piece), voxellerinin sıkı sınırlayıcı kutusuyla tanımlanır: `boxMin` (dünya/model
koordinatında en küçük dolu hücre) ve `boxSize` (kapsayıcı genişlik, yani `max - min + 1`).

Kutu içindeki bir hücrenin indeksi:

```
index = (y * boxSize.z + z) * boxSize.x + x
```

Yani **x en hızlı**, sonra **z**, sonra **y** değişir. Voxelleri bu sırada gezmek zorundasın —
koşu (run) birleştirme buna dayanıyor.

> Bu sıra keyfi değil: ekstrüzyon dolu hacimleri x boyunca serdiği için koşular böyle en uzun
> çıkıyor.

### Koşular (runs)

Aynı renkte ve **x ekseninde bitişik** hücreler tek bir koşuya toplanır. Bir koşu şu durumlarda
kırılır:

- renk değişince,
- bir boşluk gelince,
- **satır sonunda** (`x == 0`'a dönüldüğünde) — indeks `+1` olsa bile bu bir süreksizliktir,
- uzunluk `65535`'e ulaşınca.

---

## 3. VoxelBodyCodec — gövde payload'ı

Bu, karakter ve silah dosyalarının içindeki çekirdek. Tek başına da ağ payload'ı olarak kullanılır.

### Çerçeve

```
byte  format        0x01 = ham, 0x02 = Deflate'lenmiş
...   gövde         (format 0x02 ise raw DEFLATE akışı)
```

Sıkıştırma **yalnızca kazandığında** uygulanır: çıktı girdiden küçük değilse ham yazılır. Sen her
zaman `0x01` (ham) yazabilirsin — okuyucu ikisini de kabul eder. Deflate yazacaksan **zlib/gzip
başlığı değil, çıplak DEFLATE** (.NET `DeflateStream`, RFC 1951).

> Okuyucu, şişme sırasında **2 MB** tavanı uygular. Ham gövde bunun altında kalmalı.

### Gövde

```
float   voxelSize
varint  pieceCount
varint  paletteCount          (0 = palet yok, renkler ham RGB)
        paletteCount × { byte r, byte g, byte b }

pieceCount × parça:
    float   localPosition.x, .y, .z
    float   localRotation.x, .y, .z, .w        (quaternion)
    int16   boxMin.x, .y, .z
    uint16  boxSize.x, .y, .z                  (kapsayıcı; her eksen ≤ 64, 0 = boş parça)
    varint  runCount
            runCount × { varint gap, varint length, renk }
    varint  faceCount
            faceCount × { varint gap, byte face, renk }
```

**Renk alanı** paletin varlığına göre değişir:

- `paletteCount == 0` → 3 bayt ham RGB
- `1 ≤ paletteCount ≤ 256` → **1 bayt** palet indeksi
- `paletteCount > 256` → **2 bayt**, düşük bayt önce (`index & 0xFF`, sonra `index >> 8`)

Palet **bütün gövde için ortaktır** — parçalar ve yüz renkleri aynı tabloyu paylaşır. En fazla
65534 renk; daha fazlası varsa palet yazma (`paletteCount = 0`), her şey ham RGB olur.

### gap'in anlamı

İki bölümde **farklı** ölçülür ve bu ayrım kritik:

- **Koşularda:** bir önceki koşunun **BİTİŞİNDEN** itibaren (`gap = start - previousEnd`, burada
  `previousEnd = previousStart + previousLength`). İlk koşu için `previousEnd = 0`.
- **Yüzlerde:** bir önceki girdinin **İNDEKSİNDEN** itibaren (`gap = index - previousIndex`).
  İlk girdi için `previousIndex = 0`. Aynı voxel'in birden çok yüzü aynı indeksi paylaştığı için
  `gap = 0` "aynı voxel'in başka bir yüzü" demektir.

Her iki bölüm de **artan sırada** yazılmak zorundadır. Yüz girdileri `(index, face)` ikilisine göre
sıralanmalı — okuyucu tekrar eden `(index, face)` çiftini reddeder.

### Yüz numaraları

`face` alanı 0..5, şu sırayla:

| face | normal |
|---|---|
| 0 | (0, 0, −1) |
| 1 | (0, 0, +1) |
| 2 | (0, +1, 0) |
| 3 | (0, −1, 0) |
| 4 | (−1, 0, 0) |
| 5 | (+1, 0, 0) |

Yüz bölümü, voxel'in **taban rengine istisnadır**. Taban renk koşularda zaten var; hiç yüz
boyamadıysan parça başına tek bir `0` baytı yaz.

### Boş parça

Voxel'i olmayan bir parça yine de **tam bir başlık** yazar: pozisyon, rotasyon, `boxMin = 0`,
`boxSize = 0`, `runCount = 0`, `faceCount = 0`. `faceCount`'u atlamak okuyucuyu sonraki parçaların
başlığını yüz girdisi sanmaya götürür.

### Okuyucunun uyguladığı sınırlar

Bunların dışına çıkan dosya **reddedilir** (yarım uygulanmaz):

| Sınır | Değer | Nerede |
|---|---|---|
| Toplam voxel | 200 000 | her zaman |
| Parça kutusu, eksen başına | 64 | her zaman |
| Palet | 65534 renk | her zaman |
| Koşu uzunluğu | 65535 | her zaman |
| varint | 5 bayt | her zaman |
| Şişme sonrası boyut | 2 MB | **yalnızca** `0x02` (Deflate) yolunda |
| Payload boyutu | 256 KB | **yalnızca** ağdan gelen karakterlerde |

Ham (`0x01`) bir dosyanın boyutuna sınır yok — 2 MB tavanı sadece Deflate açılırken uygulanıyor,
256 KB tavanı da yalnızca `CharacterValidator`'ın gördüğü ağ payload'ında. Diskten okunan bir dosya
ikisine de takılmaz.

---

## 4. `.character` — bir karakter dosyası

İki katman: `CharacterFile` (dosya kabuğu) → `CharacterCodec` (hangi grid hangi slotta) →
`VoxelBodyCodec` (voxeller).

### 4.1 CharacterCodec payload'ı

```
"MCC"                          3 bayt ASCII
byte    1                      sürüm
varint  partCount              ≤ 64
        partCount × { varint idByteLength (≤64), UTF-8 bayt }
varint  bodyLength
        bodyLength bayt        VoxelBodyCodec payload'ı
```

**Kimlik listesi ile gövdedeki parça sayısı birebir eşit olmalı** ve **aynı sırada** olmalı —
`ids[i]` ↔ `pieces[i]`. Uyuşmazlık dosyayı geçersiz kılar.

Buradaki parçaların `localPosition` / `localRotation` alanları **yok sayılır**: bir vücut parçasının
yeri kemikten gelir. Sıfır ve birim quaternion yaz.

`partId` değerleri `CharacterRigDefinition`'daki `Part Id` alanlarıyla **birebir** aynı olmalı.
Rig'de olmayan bir kimlik **atlanır**, dosyayı geçersiz kılmaz — ama konsola
`'<id>' bu rig'de yok - atlandi` uyarısı düşer, yani yazım hatası sessiz kalmaz.

### 4.2 Dosya kabuğu

```
"MCF"                          3 bayt ASCII
byte    1                      sürüm
string  characterName          uzunluk önekli UTF-8
string  rigId
int32   payloadLength
        payloadLength bayt     CharacterCodec payload'ı (4.1)
```

`payloadLength == 0` geçerlidir: hiçbir şey modellenmemiş bir karakter.

---

## 5. `.weapons` — bir silah dosyası

Silahlar için **dosya başına bir silah** kullanılıyor (`<weaponId>.weapons`). Format birden fazla
girdi taşıyabilir; okuyucu tek girdilik dosyayı da, kimliğe göre eşleşmeyi de kabul eder.

```
"MWS"                          3 bayt ASCII
byte    1                      sürüm
int32   weaponCount            ≤ 64   (varint DEĞİL — düz int32)
        weaponCount × {
            string  weaponId
            float   leftGrip.x,  .y,  .z
            float   rightGrip.x, .y,  .z
            float   muzzle.x,    .y,  .z
            byte    hasPoints              0 veya 1
        }
int32   payloadLength
        payloadLength bayt     CharacterCodec payload'ı (4.1), anahtar = weaponId
```

Dikkat edilecekler:

- Başlıktaki sayı `int32`, karakter formatındaki `varint` değil.
- Voxel bölümü ile başlık **kimliğe göre** eşleşir, sıraya göre değil. Voxel'i olmayan bir silah
  (sadece noktalar konmuş) voxel bölümünde hiç görünmez — bu geçerlidir.
- Üç nokta **silahın kendi local uzayında, metre** cinsindendir. `hasPoints = 0` ise üçü de yok
  sayılır ve silah prefab'ının kendi grip'leri kullanılır. Sıfır gerçek bir konum olduğu için bu
  bayrak gereklidir.
- `payloadLength == 0` geçerlidir.

---

## 6. Ölçek ve konum

`voxelSize` alanı VoxelBodyCodec gövdesinde yazılır ama **karakter ve silah yolunda kullanılmaz**:

- **Karakter parçası:** ölçek `CharacterPartDefinition`'dan gelir (`Box Size` + `Voxel Size`).
  Voxel koordinatların `0..BoxSize-1` aralığında olmalı; dışarı taşanlar sunucuda **kırpılır**.
- **Silah:** ölçek `WeaponDefinition`'ın `Skin Box Size` / `Skin Voxel Size` alanlarından gelir.

Yani senin aracın `voxelSize` olarak `1.0` yazabilir; önemli olan **hücre koordinatları**.

Zorunlu çekirdeği (`Required Min` / `Required Size`) boş bırakan bir karakter parçası sunucuda
**geri doldurulur** — reddedilmez.

---

## 7. Asgari örnek (Python)

Tek parçalı, 2×2×2 dolu kırmızı bir küp içeren bir `.character`:

```python
import struct

def varint(v):
    out = bytearray()
    while v >= 0x80:
        out.append((v | 0x80) & 0xFF); v >>= 7
    out.append(v & 0xFF)
    return bytes(out)

def bstring(s):
    d = s.encode("utf-8")
    return varint(len(d)) + d

# --- VoxelBodyCodec gövdesi: 1 parça, 2x2x2, tek renk ---
size = (2, 2, 2)
body  = struct.pack("<f", 1.0)          # voxelSize
body += varint(1)                       # pieceCount
body += varint(1)                       # paletteCount
body += bytes((200, 60, 60))            # palet[0]

body += struct.pack("<fff", 0, 0, 0)            # localPosition
body += struct.pack("<ffff", 0, 0, 0, 1)        # localRotation
body += struct.pack("<hhh", 0, 0, 0)            # boxMin
body += struct.pack("<HHH", *size)              # boxSize

# x boyunca 2'lik koşular; her satır yeni koşu (satır sonu süreksizlik)
runs, prev_end = bytearray(), 0
for y in range(size[1]):
    for z in range(size[2]):
        start = (y * size[2] + z) * size[0]
        runs += varint(start - prev_end) + varint(size[0]) + bytes([0])  # palet indeksi
        prev_end = start + size[0]

body += varint(4) + bytes(runs)          # runCount = 2*2 satır
body += varint(0)                        # faceCount

payload_body = bytes([0x01]) + body      # ham çerçeve

# --- CharacterCodec ---
cc  = b"MCC" + bytes([1])
cc += varint(1)
cc += bstring("head")
cc += varint(len(payload_body)) + payload_body

# --- CharacterFile ---
f  = b"MCF" + bytes([1])
f += bstring("Test") + bstring("steve")
f += struct.pack("<i", len(cc)) + cc

open("Test.character", "wb").write(f)
```

> `varint(4)` satırındaki koşu sayısı, döngünün ürettiği koşu sayısıyla aynı olmalı. Kendi
> aracında bunu saydırarak yaz.

---

## 8. Doğrulama

Çıktını Unity'ye atmadan önce iki şeyi kontrol et:

1. **Round-trip.** Projede `Assets/Scripts/Editor/Tests/CharacterCodecTests.cs` ve
   `VoxelBodyCodecTests.cs` var. Kendi dosyanı bir teste verip `TryDecode`'un `true` döndüğünü ve
   voxel sayısının beklediğin kadar olduğunu doğrula.
2. **Reddedilirse sessiz kalmaz.** Konsolda şunlardan biri düşer:
   - `[CharacterFile] Dosya okunamadi: ...`
   - `[CharacterStorage] '...' bir karakter dosyasi degil ya da bozuk.`
   - `[WeaponSkinFile] Dosya okunamadi: ...`
   - `[PlayerCharacterAppearance] Karakter reddedildi (owner N): ...`

En sık yapılan üç hata:

- **Satır sonunda koşuyu kırmamak.** `x` sarmalandığında indeks `+1` olsa da yeni koşu başlamalı.
- **gap'i iki bölümde aynı sanmak.** Koşularda önceki koşunun *bitişinden*, yüzlerde önceki
  girdinin *indeksinden*.
- **Boş parçada `faceCount`'u atlamak.** Okuyucu parça başına koşulsuz bir tane okur.

---

## 9. Sürüm notu

Sürüm baytları (`MCC`/`MCF`/`MWS` sonrası) şu an **1**. Okuyucu farklı bir sürümü **reddeder** —
sessizce eski gibi okumaya çalışmaz. Format değişirse bu belge ve o bayt birlikte değişir.

`VoxelBodyCodec`'in kendi sürüm baytı yoktur; ilk baytı çerçeve bayrağıdır (`0x01`/`0x02`).
