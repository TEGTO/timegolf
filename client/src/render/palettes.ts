/** Per-level color scheme, picked by `LevelDto.palette`. */
export type Palette = {
  name: string;
  grass: string;
  stripe: string;
  sand: string;
  stone: string;
  wood: string;
  cliff: string;
  sky: string;
  ground: string;
  bgTop: string;
  bgBottom: string;
};

const palettes: Palette[] = [
  {
    name: 'Meadow',
    grass: '#6cc644',
    stripe: '#7cd254',
    sand: '#f6e2a0',
    stone: '#f4efe4',
    wood: '#e0894a',
    cliff: '#c99a6c',
    sky: '#ffffff',
    ground: '#a8c98a',
    bgTop: '#5fb8f0',
    bgBottom: '#e2f5ff'
  },
  {
    name: 'Beach',
    grass: '#57cc84',
    stripe: '#68d894',
    sand: '#ffe3a8',
    stone: '#fff8ea',
    wood: '#33b5d6',
    cliff: '#e8c08a',
    sky: '#ffffff',
    ground: '#f0d9a6',
    bgTop: '#3ec4f2',
    bgBottom: '#e3fbff'
  },
  {
    name: 'Autumn',
    grass: '#98c548',
    stripe: '#a8d158',
    sand: '#f8d69c',
    stone: '#f6ebdc',
    wood: '#e3672f',
    cliff: '#cf8a55',
    sky: '#fff8ec',
    ground: '#e0b27a',
    bgTop: '#7cc6f5',
    bgBottom: '#ffeacc'
  },
  {
    name: 'Candy',
    grass: '#78d8a2',
    stripe: '#8ae2b0',
    sand: '#ffd9ea',
    stone: '#ffffff',
    wood: '#ff7fb8',
    cliff: '#e6a8c9',
    sky: '#ffffff',
    ground: '#f6c8de',
    bgTop: '#ffb8dc',
    bgBottom: '#fff1d8'
  },
  {
    name: 'Snow',
    grass: '#8ed4a9',
    stripe: '#a0deb8',
    sand: '#ffffff',
    stone: '#eaf2f9',
    wood: '#e8584f',
    cliff: '#bcd3e8',
    sky: '#ffffff',
    ground: '#dce9f4',
    bgTop: '#82c4f2',
    bgBottom: '#f5fbff'
  }
];

/** Wraps any palette number (including negative) onto the palette list. */
export const paletteIndex = (palette: number) => ((palette % palettes.length) + palettes.length) % palettes.length;

export const paletteOf = (palette: number) => palettes[paletteIndex(palette)];
