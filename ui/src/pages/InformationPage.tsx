import { Box } from "@mui/material";
import { useLocation } from "react-router-dom";

interface IInformationPageProps {
  message?: string;
}

const InformationPage: React.FC<IInformationPageProps> = ({ message }) => {
  const location = useLocation();

  return <Box sx={{ p: 2 }}>{message ?? location.state.message}</Box>;
};

export default InformationPage;
