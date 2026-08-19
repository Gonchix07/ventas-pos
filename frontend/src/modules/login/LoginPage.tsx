import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [usuario, setUsuario] = useState("admin");
  const [clave, setClave] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      await login(usuario, clave);
      navigate("/");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error de autenticación");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-shell">
      <form className="login-card" onSubmit={onSubmit}>
        <div className="brand">
          <span className="brand-mark">POS</span>
          <span className="brand-sub">Mayorista</span>
          <img src="/icono.png" alt="POS Mayorista" className="brand-logo" />
        </div>
        <h1>Ingresar</h1>
        <label>
          Usuario
          <input value={usuario} onChange={(e) => setUsuario(e.target.value)} autoFocus />
        </label>
        <label>
          Clave
          <input type="password" value={clave} onChange={(e) => setClave(e.target.value)} />
        </label>
        {error && <p className="error">{error}</p>}
        <button type="submit" disabled={loading}>
          {loading ? "Ingresando…" : "Ingresar"}
        </button>
        <p className="hint">Usuario inicial: <code>admin</code> · clave de seed <code>Admin123!</code></p>
      </form>
    </div>
  );
}
