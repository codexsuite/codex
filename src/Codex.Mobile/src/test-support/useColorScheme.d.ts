// react-native ships this internal module without types; the specs mock it to set the device colour scheme.
declare module 'react-native/Libraries/Utilities/useColorScheme' {
  const useColorScheme: () => 'light' | 'dark' | null | undefined;
  export default useColorScheme;
}
