import {useEffect, useState} from 'react';
import {FLASH_MS} from '../constants';

/** Message that clears itself after a moment. */
export function useFlash() {
  const [text, setText] = useState<string | null>(null);
  useEffect(() => {
    if (!text) return;
    const id = setTimeout(() => setText(null), FLASH_MS);
    return () => clearTimeout(id);
  }, [text]);
  return [text, setText] as const;
}
