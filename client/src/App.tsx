import {useEffect, useRef, useState} from 'react';
import {bindInput} from './input';
import {aim, resuming, shoot, skip} from './net';
import {GameScene} from './render/Scene';
import {actions, useStore} from './store';
import {Hud} from './ui/Hud';
import {Leaderboard} from './ui/Leaderboard';
import {MainScreen} from './ui/MainScreen';
import {ReadyRoom} from './ui/ReadyRoom';
import {Results} from './ui/Results';
import {Timeline} from './ui/Timeline';
import {Toast} from './ui/Toast';

function Game() {
  const host = useRef<HTMLDivElement>(null);
  const inLobby = useStore((s) => s.phase === 'Lobby');

  useEffect(() => {
    const scene = new GameScene(host.current!, shoot, aim);
    const unbind = bindInput(scene.canvas, {
      aim: {
        begin: (at) => scene.beginAim(at),
        move: (_, to) => scene.dragAim(to),
        end: (dragged) => scene.endAim(dragged)
      },
      freeRotate: {move: (from, to) => scene.rotateBy(to.clientX - from.clientX)},
      pan: {move: (from, to) => scene.pan(from, to)},
      cancelAim: () => scene.cancelAim(),
      present: () => actions.view(null),
      viewPrev: () => actions.step(-1),
      viewNext: () => actions.step(1),
      skip,
      focus: () => scene.focus(),
      rotateLeft: () => scene.rotate(-1),
      rotateRight: () => scene.rotate(1),
      mute: actions.toggleMute,
      zoom: (deltaY) => scene.zoomBy(deltaY)
    });
    return () => {
      unbind();
      scene.dispose();
    };
  }, []);

  return (
    <div className='game'>
      <div className='viewport' ref={host} />
      <Hud />
      {inLobby ? <ReadyRoom /> : <Leaderboard />}
      <Timeline />
      <Results />
      <Toast />
    </div>
  );
}

export default function App() {
  const joined = useStore((s) => s.joined);
  const [waiting, setWaiting] = useState(resuming !== null);
  useEffect(() => void resuming?.finally(() => setWaiting(false)), []);
  if (joined) return <Game />;
  return waiting ? <div className='sky' /> : <MainScreen />;
}
