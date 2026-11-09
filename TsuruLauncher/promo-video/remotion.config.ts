import { Config } from '@remotion/cli/config';

Config.setConcurrency(2);
Config.setCodec('h264');
Config.setPixelFormat('yuv420p');
Config.setCrf(18);
Config.setImageFormat('jpeg');
Config.setJpegQuality(92);
