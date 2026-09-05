import type { Config } from "tailwindcss";

const config: Config = {
  darkMode: "class",
  content: ["./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        background: "hsl(240 10% 4%)",
        foreground: "hsl(0 0% 95%)",
        card: { DEFAULT: "hsl(240 8% 8%)", foreground: "hsl(0 0% 95%)", border: "hsl(240 6% 15%)" },
        primary: { DEFAULT: "hsl(210 100% 48%)", foreground: "hsl(0 0% 100%)" },
        secondary: { DEFAULT: "hsl(180 70% 47%)", foreground: "hsl(0 0% 100%)" },
        accent: { DEFAULT: "hsl(320 85% 55%)", foreground: "hsl(0 0% 100%)" },
        muted: { DEFAULT: "hsl(240 6% 12%)", foreground: "hsl(0 0% 60%)" },
        neon: { cyan: "#00bcf2", magenta: "#e3008c", blue: "#0078d4", glow: "0 0 20px rgba(0,188,242,0.3)" },
        border: "hsl(240 6% 15%)",
        input: "hsl(240 6% 15%)",
        ring: "hsl(210 100% 48%)",
        destructive: { DEFAULT: "hsl(0 70% 55%)", foreground: "hsl(0 0% 100%)" },
      },
      borderRadius: { lg: "1rem", md: "0.75rem", sm: "0.5rem" },
      fontFamily: {
        display: ["'Clash Display'", "sans-serif"],
        mono: ["'IBM Plex Mono'", "monospace"],
        sans: ["'Inter'", "sans-serif"],
      },
      animation: {
        "glow-pulse": "glow-pulse 2s ease-in-out infinite",
        "slide-up": "slide-up 0.5s ease-out",
        "fade-in": "fade-in 0.3s ease-out",
      },
      keyframes: {
        "glow-pulse": { "0%, 100%": { boxShadow: "0 0 20px rgba(0,188,242,0.2)" }, "50%": { boxShadow: "0 0 40px rgba(0,188,242,0.4)" } },
        "slide-up": { "0%": { transform: "translateY(20px)", opacity: "0" }, "100%": { transform: "translateY(0)", opacity: "1" } },
        "fade-in": { "0%": { opacity: "0" }, "100%": { opacity: "1" } },
      },
    },
  },
  plugins: [require("tailwindcss-animate")],
};
export default config;
