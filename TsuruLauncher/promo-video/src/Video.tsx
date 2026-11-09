import { AbsoluteFill, Img, interpolate, spring, useCurrentFrame, useVideoConfig, Audio } from 'remotion';

const scenes = [
  { img: 'final-1-hero.png', dur: 210 },      // 7s
  { img: 'final-2-login.png', dur: 240 },     // 8s
  { img: 'final-3-ui.png', dur: 330 },        // 11s
  { img: 'final-4-theme.png', dur: 240 },     // 8s
  { img: 'final-5-skins.png', dur: 240 },     // 8s
  { img: 'final-6-download.png', dur: 210 },  // 7s
  { img: 'final-7-end.png', dur: 150 },       // 5s
];

export const Video: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  let acc = 0;
  let currentScene = 0;
  let sceneStart = 0;

  for (let i = 0; i < scenes.length; i++) {
    if (frame < acc + scenes[i].dur) {
      currentScene = i;
      sceneStart = acc;
      break;
    }
    acc += scenes[i].dur;
  }

  const sceneFrame = frame - sceneStart;
  const sceneDur = scenes[currentScene].dur;

  // 入场动画：scale + opacity
  const fadeIn = interpolate(sceneFrame, [0, 20], [0, 1], { extrapolateRight: 'clamp' });
  const scaleIn = spring({
    frame: sceneFrame,
    fps,
    config: { damping: 30, stiffness: 120 },
    from: 0.96,
    to: 1,
  });

  // 离场动画
  const fadeOut = interpolate(sceneFrame, [sceneDur - 15, sceneDur], [1, 0], {
    extrapolateLeft: 'clamp',
  });

  return (
    <AbsoluteFill style={{ background: '#0F1216' }}>
      <Audio src="staticFile('music.mp3')" />
      <AbsoluteFill
        style={{
          opacity: fadeIn * fadeOut,
          transform: `scale(${scaleIn})`,
        }}
      >
        <Img src={staticFile(scenes[currentScene].img)} style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
