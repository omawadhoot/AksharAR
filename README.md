<div align="center">

<img src="Assets/Icons/AppIcon.png" alt="AksharAR App Icon" width="128" />

# AksharAR
### Devanagari-Centric AR Assistive Framework for Dyslexic Children

*Transforming physical Hindi and Marathi schoolbooks into interactive, dyslexia-friendly reading experiences through Spatial Augmented Reality.*

[![Unity](https://img.shields.io/badge/Engine-Unity%203D-000000?style=for-the-badge&logo=unity&logoColor=white)](https://unity.com/)
[![ARCore](https://img.shields.io/badge/Spatial%20AR-Google%20ARCore-4285F4?style=for-the-badge&logo=google&logoColor=white)](https://developers.google.com/ar)
[![Android](https://img.shields.io/badge/Platform-Android-3DDC84?style=for-the-badge&logo=android&logoColor=white)](https://developer.android.com/)
[![Accessibility](https://img.shields.io/badge/Focus-Assistive%20EdTech%20%26%20HCI-7C3AED?style=for-the-badge)](#)

</div>

---

## Why AksharAR?

Most assistive reading tools for dyslexia are built exclusively for digital screens and Latin scripts. However, millions of children in India learn to read using **printed textbooks** in **Devanagari (Hindi & Marathi)**, where unique orthographic structures create severe visual crowding:

| The Devanagari Reading Barrier | How AksharAR Solves It |
| :--- | :--- |
| **Continuous Overhead Line (*Shirorekha*)**<br>Fuses characters across an entire word into a single dense block. | **Alternating Syllable Chromatics**<br>Visually separates adjacent syllables using high-contrast, color-blind safe palettes. |
| **Complex Conjuncts (*Samyuktaksharas*)**<br>Half-consonants merged across *Halant* boundaries confuse phonetic decoding. | **Phonetic *Akshara* Segmentation**<br>Keeps consonant clusters and vowel *matras* bound as intact pronounceable units. |
| **Static Printed Textbooks**<br>Paper textbooks offer zero font customization, line spacing, or audio feedback. | **In-Situ Spatial AR & Speech**<br>Projects stabilized 3D reading overlays and focus rulers directly over the physical page with tap-to-read speech. |

---

## Core Capabilities

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>📱 Ergonomic Guided Capture</h3>
      <p>Designed around child-friendly Human-Computer Interaction (HCI) principles. Features an adjustable reading viewfinder for both short poetry stanzas and dense textbook chapters, paired with gentle posture and motion guidance for effortless scanning.</p>
    </td>
    <td width="50%" valign="top">
      <h3>🧩 Devanagari Syllable Intelligence</h3>
      <p>Unlike standard character splitters that break Indic words apart, AksharAR understands Devanagari linguistics. It preserves conjunct clusters and vowel diacritics so every highlighted block matches a true spoken syllable (<i>Akshara</i>).</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🥽 Pose-Stabilized Spatial AR</h3>
      <p>Anchors interactive reading lines and focus rulers right onto the physical textbook page in 3D space. Real-time spatial smoothing filters out natural hand tremors so the overlay stays steady while reading.</p>
    </td>
    <td width="50%" valign="top">
      <h3>🔊 Multisensory & Adaptive Profiles</h3>
      <p>Pairs visual syllable segmentation with synchronized Hindi and Marathi Text-to-Speech (TTS). Learners can customize letter spacing, line height, background tints, and dyslexia-friendly Devanagari typography.</p>
    </td>
  </tr>
</table>

---

## Phonetic *Akshara* Breakdown in Action

Instead of naive character-by-character splitting (which breaks half-letters and detaches vowel signs), **AksharAR** groups words into true pronounceable syllables:

<div align="center">

| Printed Textbook Word | Naive Character Split (Broken) | AksharAR Syllable Overlay (Intact *Aksharas*) |
| :---: | :---: | :---: |
| **नमस्कार** | न + म + स + ् + क + ा + र | **`[ न ]`** • **`[ म ]`** • **`[ स्का ]`** • **`[ र ]`** 🔊 |
| **अक्षर** | अ + क + ् + ष + र | **`[ अ ]`** • **`[ क्ष ]`** • **`[ र ]`** 🔊 |
| **प्रार्थना** | प + ् + र + ा + र + ् + थ + न + ा | **`[ प्रा ]`** • **`[ र्थ ]`** • **`[ ना ]`** 🔊 |

</div>

---

## How It Works

```mermaid
flowchart LR
    A(["📖 Physical Textbook\n(Hindi / Marathi)"]) ==> B["📸 Guided Page Capture\n& Cloud Vision OCR"]
    B ==> C["⚙️ Orthographic Devanagari\nSyllable Segmentation"]
    C ==> D["🥽 3D World-Space AR Overlay\n+ Focus Ruler"]
    C ==> E["🔊 Synchronized\nHindi & Marathi TTS"]
```

1. **Frame & Scan:** Point the smartphone camera at any printed Hindi or Marathi page using the guided reading window.
2. **Linguistic Breakdown:** Detected lines are segmented into phonetically intact *Aksharas* and styled according to the child's accessibility profile.
3. **Read & Listen in AR:** Color-coded syllable cards and a focus ruler lock onto the physical page. Tap any word or line to hear clear native pronunciation while following along visually.

---

## Built With

<div align="center">

![Unity](https://img.shields.io/badge/Unity%203D-20232A?style=flat-square&logo=unity&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=flat-square&logo=csharp&logoColor=white)
![ARCore](https://img.shields.io/badge/AR%20Foundation%20%26%20ARCore-4285F4?style=flat-square&logo=google&logoColor=white)
![Google Cloud Vision](https://img.shields.io/badge/Google%20Cloud%20Vision-EA4335?style=flat-square&logo=googlecloud&logoColor=white)
![Node.js](https://img.shields.io/badge/Node.js-339933?style=flat-square&logo=nodedotjs&logoColor=white)
![Android](https://img.shields.io/badge/Android%20TTS-3DDC84?style=flat-square&logo=android&logoColor=black)

</div>

---

## Team & Mentorship

- **Team Members:** Om Awadhoot, Lohit Bhaware, Atharv Patil, Parth Thakre
- **Project Guide:** Prof. Swati Tiwari
- **Location:** Nagpur, Maharashtra, India
