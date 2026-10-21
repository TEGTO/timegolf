import {useEffect} from 'react';
import {FLASH_MS} from '../constants';
import {useStore} from '../store';

export function Toast() {
  const toast = useStore((s) => s.toast);
  useEffect(() => {
    if (!toast) return;
    const id = setTimeout(() => useStore.setState({toast: null}), FLASH_MS);
    return () => clearTimeout(id);
  }, [toast]);
  return toast ? (
    <div className='toast' key={toast.key}>
      {toast.text}
    </div>
  ) : null;
}
