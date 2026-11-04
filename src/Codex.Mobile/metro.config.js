// Metro config (#35). Expo's defaults cover the pnpm workspace; the one addition is Node's `buffer`, which
// whisper.rn (through safe-buffer) requires from deep inside node_modules, where React Native has no such module.
const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);
config.resolver.extraNodeModules = {
  ...config.resolver.extraNodeModules,
  buffer: require.resolve('buffer/'),
};

module.exports = config;
