/** Clouds, hill, flag and a ball rolling into the cup; purely decorative. */
export function Scenery() {
  return (
    <div className='scenery' aria-hidden>
      <i className='sun' />
      <i className='cloud c1' />
      <i className='cloud c2' />
      <i className='cloud c3' />
      <div className='hill back' />
      <div className='hill front'>
        <i className='cup' />
        <i className='pole' />
        <i className='pennant' />
        <i className='roller' />
      </div>
    </div>
  );
}
